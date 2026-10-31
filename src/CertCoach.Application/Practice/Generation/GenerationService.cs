using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Application.Common;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Practice.Generation;

/// <summary>Starts, follows and cancels generation runs, and works through a run for the background worker.</summary>
public sealed class GenerationService(
    ICertCoachDbContext db, ICurrentUser user, UserClock clock, IGenerationSignal signal, ItemGenerator generator, AiCapabilities capabilities)
{
    public const int RecentRuns = 5;

    public async Task<GenerationJobView> StartAsync(GenerateItemsRequest request, CancellationToken ct)
    {
        var certification = await LoadCertificationAsync(request.CertificationId, ct);
        var objectives = certification.Domains
            .Where(d => request.DomainId is null || d.Id == request.DomainId)
            .SelectMany(d => d.SkillAreas)
            .SelectMany(s => s.Objectives)
            .Where(o => request.ObjectiveId is null || o.Id == request.ObjectiveId)
            .Select(o => o.Id)
            .ToList();
        if (objectives.Count == 0)
        {
            throw new DomainException("That domain or objective isn't in this certification's outline.");
        }

        if (await db.GenerationJobs.AnyAsync(
                j => j.CertificationId == certification.Id && (j.Status == GenerationStatus.Queued || j.Status == GenerationStatus.Running), ct))
        {
            throw new ConflictException("Questions are already being written for this certification. Wait for that run, or cancel it.");
        }

        var job = GenerationJob.Create(user.UserId, certification.Id, objectives, request.PerObjective, clock.Now);
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return ToView(job);
    }

    public async Task<GenerationJobView> GetAsync(Guid id, CancellationToken ct) => ToView(await LoadAsync(id, ct));

    public async Task<IReadOnlyList<GenerationJobView>> ListAsync(Guid certificationId, CancellationToken ct) =>
        (await db.GenerationJobs
            .Where(j => j.OwnerId == user.UserId && j.CertificationId == certificationId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(RecentRuns)
            .ToListAsync(ct))
        .Select(ToView)
        .ToList();

    /// <summary>Stops the run before its next objective. Items already written are kept.</summary>
    public async Task<GenerationJobView> CancelAsync(Guid id, CancellationToken ct)
    {
        var job = await LoadAsync(id, ct);
        job.Cancel(clock.Now);
        await db.SaveChangesAsync(ct);
        return ToView(job);
    }

    /// <summary>
    /// Works through a claimed run, saving the items and progress after each objective. Runs in a scope whose
    /// <see cref="BackgroundUser"/> is the run's owner.
    /// </summary>
    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.GenerationJobs.SingleAsync(j => j.Id == jobId && j.OwnerId == user.UserId, ct);
        try
        {
            var certification = await LoadCertificationAsync(job.CertificationId, ct);
            var contexts = Contexts(certification);

            while (job.NextObjective is { } objectiveId)
            {
                // A cancel from the API lands between objectives.
                var status = await db.GenerationJobs.Where(j => j.Id == jobId).Select(j => j.Status).SingleAsync(ct);
                if (status is not (GenerationStatus.Queued or GenerationStatus.Running))
                {
                    return;
                }

                var result = new ObjectiveResult(0, 0, HadMaterial: true);
                if (contexts.TryGetValue(objectiveId, out var context))
                {
                    var existing = await db.PracticeItems
                        .Where(i => i.OwnerId == user.UserId && i.ObjectiveId == objectiveId)
                        .Select(i => i.Prompt)
                        .ToListAsync(ct);
                    try
                    {
                        result = await generator.GenerateAsync(context with { ExistingPrompts = existing }, job.PerObjective, ct);
                    }
                    catch (DomainException)
                    {
                        // The model declined this objective; the others may still work.
                    }
                }

                job.RecordObjective(result.Created, result.Rejected, result.HadMaterial, clock.Now);
                await db.SaveChangesAsync(ct);
            }

            job.Complete(clock.Now);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is not DbUpdateConcurrencyException)
        {
            // Items not yet saved belong to the objective that failed; drop them with it.
            db.ChangeTracker.Clear();
            var failed = await db.GenerationJobs.SingleAsync(j => j.Id == jobId, ct);
            failed.Fail(e is DomainException ? e.Message : $"Writing questions failed: {e.Message}", clock.Now);
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task<GenerationJob> LoadAsync(Guid id, CancellationToken ct) =>
        await db.GenerationJobs.SingleOrDefaultAsync(j => j.Id == id && j.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Generation run");

    private async Task<Certification> LoadCertificationAsync(Guid id, CancellationToken ct) =>
        await db.Certifications
            .Include(c => c.Domains)
            .ThenInclude(d => d.SkillAreas)
            .ThenInclude(s => s.Objectives)
            .SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Certification");

    private static Dictionary<Guid, ObjectiveContext> Contexts(Certification certification) =>
        certification.Domains
            .SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives.Select(o => new ObjectiveContext(
                certification.Id, o.Id, certification.ExamCode, certification.Name, d.Name, s.Name, o.Text, []))))
            .ToDictionary(c => c.ObjectiveId);

    private GenerationJobView ToView(GenerationJob j) => new(
        j.Id,
        j.CertificationId,
        j.Status,
        j.ObjectiveIds.Count,
        j.ObjectivesDone,
        j.PerObjective,
        j.ItemsCreated,
        j.ItemsRejected,
        j.ObjectivesWithoutMaterial,
        capabilities.ChatAvailable,
        j.Error,
        j.CreatedAt,
        j.FinishedAt);
}
