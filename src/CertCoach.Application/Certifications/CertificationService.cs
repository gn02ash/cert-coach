using CertCoach.Application.Abstractions;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Common;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Common;
using CertCoach.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Certifications;

public sealed class CertificationService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    public async Task<IReadOnlyList<CertificationSummary>> ListAsync(CancellationToken ct)
    {
        var certifications = await WithOutline()
            .Where(c => c.OwnerId == user.UserId)
            .OrderBy(c => c.ExamCode)
            .ToListAsync(ct);
        var activePlans = await ActivePlansByCertificationAsync(ct);

        return certifications
            .Select(c => new CertificationSummary(
                c.Id,
                c.ExamCode,
                c.Name,
                c.Vendor,
                c.Status,
                c.ReplacedByExamCode,
                c.Domains.Count,
                c.ObjectiveCount,
                activePlans.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<CertificationDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var certification = await LoadAsync(id, ct);
        var activePlans = await ActivePlansByCertificationAsync(ct);
        return ToDetail(certification, activePlans.GetValueOrDefault(id));
    }

    public async Task<CertificationDetail> CreateAsync(SaveCertificationRequest request, CancellationToken ct)
    {
        await EnsureExamCodeIsFreeAsync(request.ExamCode, exceptId: null, ct);

        var certification = Certification.Create(user.UserId, request.ExamCode, request.Name, request.Vendor, clock.Now);
        Apply(certification, request);
        ApplyOutline(certification, request.Outline);

        db.Certifications.Add(certification);
        await db.SaveChangesAsync(ct);
        return ToDetail(certification, activePlanId: null);
    }

    public async Task<CertificationDetail> UpdateAsync(Guid id, SaveCertificationRequest request, CancellationToken ct)
    {
        var certification = await LoadAsync(id, ct);
        await EnsureExamCodeIsFreeAsync(request.ExamCode, exceptId: id, ct);
        var activePlanId = (await ActivePlansByCertificationAsync(ct)).GetValueOrDefault(id);

        Apply(certification, request);
        if (!SameOutline(certification, request.Outline))
        {
            // Sessions reference objective IDs, so an active plan would silently lose its content.
            if (activePlanId is not null)
            {
                throw new ConflictException("Archive the active study plan before changing this outline.");
            }

            certification.ClearOutline();
            ApplyOutline(certification, request.Outline);
        }

        await db.SaveChangesAsync(ct);
        return ToDetail(certification, activePlanId);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var certification = await LoadAsync(id, ct);
        if (await db.StudyPlans.AnyAsync(p => p.CertificationId == id, ct))
        {
            throw new ConflictException("This certification has study plans. Delete or archive them first.");
        }

        db.Certifications.Remove(certification);
        await db.SaveChangesAsync(ct);
    }

    internal async Task<Certification> LoadAsync(Guid id, CancellationToken ct) =>
        await WithOutline().SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Certification");

    private IQueryable<Certification> WithOutline() =>
        db.Certifications
            .Include(c => c.Domains)
            .ThenInclude(d => d.SkillAreas)
            .ThenInclude(s => s.Objectives);

    private async Task<Dictionary<Guid, Guid?>> ActivePlansByCertificationAsync(CancellationToken ct) =>
        await db.StudyPlans
            .Where(p => p.OwnerId == user.UserId && p.Status == PlanStatus.Active)
            .ToDictionaryAsync(p => p.CertificationId, p => (Guid?)p.Id, ct);

    private async Task EnsureExamCodeIsFreeAsync(string examCode, Guid? exceptId, CancellationToken ct)
    {
        var normalized = (examCode ?? string.Empty).Trim().ToUpperInvariant();
        if (await db.Certifications.AnyAsync(c => c.OwnerId == user.UserId && c.ExamCode == normalized && c.Id != exceptId, ct))
        {
            throw new ConflictException($"You already have a certification with exam code {normalized}.");
        }
    }

    private static void Apply(Certification certification, SaveCertificationRequest request)
    {
        certification.UpdateDetails(request.ExamCode, request.Name, request.Vendor, request.OutlineUrl, request.OutlineVersionDate);
        certification.SetStatus(request.Status, request.ReplacedByExamCode);
    }

    private static void ApplyOutline(Certification certification, IReadOnlyList<DomainDraft>? outline)
    {
        foreach (var domainDraft in outline ?? [])
        {
            var domain = certification.AddDomain(domainDraft.Name, domainDraft.WeightMinPercent, domainDraft.WeightMaxPercent);
            foreach (var areaDraft in domainDraft.SkillAreas ?? [])
            {
                var area = domain.AddSkillArea(areaDraft.Name);
                foreach (var objective in areaDraft.Objectives ?? [])
                {
                    area.AddObjective(objective);
                }
            }
        }

        if (certification.ObjectiveCount == 0)
        {
            throw new DomainException("The outline needs at least one domain with objectives.");
        }
    }

    private static bool SameOutline(Certification certification, IReadOnlyList<DomainDraft>? outline) =>
        Fingerprint(certification.Domains.Select(d => new DomainDraft(
            d.Name,
            d.WeightMinPercent,
            d.WeightMaxPercent,
            d.SkillAreas.Select(s => new SkillAreaDraft(s.Name, s.Objectives.Select(o => o.Text).ToList())).ToList())))
        == Fingerprint(outline ?? []);

    private static string Fingerprint(IEnumerable<DomainDraft> outline) =>
        string.Join('\n', outline.SelectMany(d =>
            new[] { $"D|{d.Name.Trim()}|{d.WeightMinPercent}|{d.WeightMaxPercent}" }.Concat(
                (d.SkillAreas ?? []).SelectMany(s =>
                    new[] { $"S|{s.Name.Trim()}" }.Concat((s.Objectives ?? []).Select(o => $"O|{o.Trim()}"))))));

    internal static CertificationDetail ToDetail(Certification c, Guid? activePlanId) => new(
        c.Id,
        c.ExamCode,
        c.Name,
        c.Vendor,
        c.Status,
        c.ReplacedByExamCode,
        c.OutlineUrl,
        c.OutlineVersionDate,
        activePlanId,
        c.Domains
            .Select(d => new DomainView(
                d.Id,
                d.Name,
                d.WeightMinPercent,
                d.WeightMaxPercent,
                d.SkillAreas
                    .Select(s => new SkillAreaView(s.Id, s.Name, s.Objectives.Select(o => new ObjectiveView(o.Id, o.Text)).ToList()))
                    .ToList()))
            .ToList());
}
