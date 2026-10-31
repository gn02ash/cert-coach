using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Practice;

/// <summary>Lists and edits practice items; generation and reviews have their own services.</summary>
public sealed class PracticeService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    public async Task<IReadOnlyList<PracticeItemView>> ListAsync(Guid certificationId, Guid? objectiveId, CancellationToken ct)
    {
        var objectives = await ObjectiveTextsAsync(certificationId, ct);
        var items = await db.PracticeItems
            .Where(i => i.OwnerId == user.UserId && i.CertificationId == certificationId)
            .Where(i => objectiveId == null || i.ObjectiveId == objectiveId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
        return await ToViewsAsync(items, objectives, ct);
    }

    public async Task<PracticeItemView> CreateAsync(CreatePracticeItemRequest request, CancellationToken ct)
    {
        var objectives = await ObjectiveTextsAsync(request.CertificationId, ct);
        if (request.ObjectiveId is { } objectiveId && !objectives.ContainsKey(objectiveId))
        {
            throw new DomainException("That objective isn't in this certification's outline.");
        }

        var item = PracticeItem.Create(
            user.UserId,
            request.CertificationId,
            request.ObjectiveId,
            request.Kind,
            PracticeItemOrigin.Manual,
            request.Prompt,
            request.Options,
            request.CorrectOptions,
            request.Answer,
            sources: null,
            clock.Now);
        db.PracticeItems.Add(item);
        await db.SaveChangesAsync(ct);
        return (await ToViewsAsync([item], objectives, ct))[0];
    }

    public async Task<PracticeItemView> UpdateAsync(Guid id, UpdatePracticeItemRequest request, CancellationToken ct)
    {
        var item = await LoadAsync(id, ct);
        item.Edit(request.Prompt, request.Options, request.CorrectOptions, request.Answer);
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(item, ct);
    }

    public async Task<PracticeItemView> SetStatusAsync(Guid id, SetPracticeItemStatusRequest request, CancellationToken ct)
    {
        var item = await LoadAsync(id, ct);
        item.SetStatus(request.Status);
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(item, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        db.PracticeItems.Remove(await LoadAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task<PracticeItem> LoadAsync(Guid id, CancellationToken ct) =>
        await db.PracticeItems.SingleOrDefaultAsync(i => i.Id == id && i.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Practice item");

    /// <summary>The certification's objectives by ID; also checks the certification is the learner's.</summary>
    private async Task<Dictionary<Guid, string>> ObjectiveTextsAsync(Guid certificationId, CancellationToken ct)
    {
        if (!await db.Certifications.AnyAsync(c => c.Id == certificationId && c.OwnerId == user.UserId, ct))
        {
            throw new NotFoundException("Certification");
        }

        return await db.Certifications
            .Where(c => c.Id == certificationId)
            .SelectMany(c => c.Domains)
            .SelectMany(d => d.SkillAreas)
            .SelectMany(s => s.Objectives)
            .ToDictionaryAsync(o => o.Id, o => o.Text, ct);
    }

    private async Task<PracticeItemView> ToViewAsync(PracticeItem item, CancellationToken ct) =>
        (await ToViewsAsync([item], await ObjectiveTextsAsync(item.CertificationId, ct), ct))[0];

    private async Task<List<PracticeItemView>> ToViewsAsync(
        IReadOnlyList<PracticeItem> items, Dictionary<Guid, string> objectives, CancellationToken ct)
    {
        var documentIds = items.SelectMany(i => i.Sources).Select(s => s.DocumentId).Distinct().ToList();
        var titles = documentIds.Count == 0
            ? []
            : (await db.Documents.Where(d => documentIds.Contains(d.Id)).ToListAsync(ct)).ToDictionary(d => d.Id, d => d.Title);

        return items.Select(i => new PracticeItemView(
                i.Id,
                i.CertificationId,
                i.ObjectiveId,
                i.ObjectiveId is { } o ? objectives.GetValueOrDefault(o) : null,
                i.Kind,
                i.Origin,
                i.Status,
                i.Prompt,
                i.Options,
                i.CorrectOptions,
                i.Answer,
                i.Sources.Select(s => new ItemSourceView(s.DocumentId, titles.GetValueOrDefault(s.DocumentId), s.Page, s.Quote)).ToList(),
                i.DueOn,
                i.Repetitions,
                i.CreatedAt))
            .ToList();
    }
}
