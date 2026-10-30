using CertCoach.Application.Abstractions;
using CertCoach.Application.Certifications;
using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using CertCoach.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Plans;

public sealed class PlanService(
    ICertCoachDbContext db, ICurrentUser user, UserClock clock, CertificationService certifications)
{
    /// <summary>Shows what a plan would look like without saving it.</summary>
    public async Task<PlanPreview> PreviewAsync(PlanRequest request, CancellationToken ct)
    {
        var certification = await certifications.LoadAsync(request.CertificationId, ct);
        var settings = ToSettings(request);
        var result = PlanScheduler.Generate(settings, StudyPlan.OutlineOf(certification));
        var cadence = SessionCadence.For(settings.HoursPerWeek, settings.StudyDays);
        var index = new OutlineIndex(certification);

        return new PlanPreview(
            cadence.SessionMinutes,
            cadence.SessionsPerWeek,
            result.Sessions.Select((s, i) => index.ToView(s, null, i, SessionStatus.Planned, null)).ToList(),
            result.Warnings,
            result.Shortfall);
    }

    public async Task<PlanView> CreateAsync(PlanRequest request, CancellationToken ct)
    {
        var certification = await certifications.LoadAsync(request.CertificationId, ct);
        if (await db.StudyPlans.AnyAsync(
                p => p.OwnerId == user.UserId && p.CertificationId == certification.Id && p.Status == PlanStatus.Active, ct))
        {
            throw new ConflictException($"There is already an active plan for {certification.ExamCode}. Archive it first.");
        }

        var (plan, warnings) = StudyPlan.Create(user.UserId, certification, ToSettings(request), clock.Now);
        db.StudyPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return ToView(plan, new OutlineIndex(certification), certification.ExamCode, certification.Name, warnings);
    }

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(CancellationToken ct)
    {
        var plans = await db.StudyPlans
            .Include(p => p.Sessions)
            .Where(p => p.OwnerId == user.UserId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        var names = await db.Certifications
            .Where(c => c.OwnerId == user.UserId)
            .ToDictionaryAsync(c => c.Id, c => new { c.ExamCode, c.Name }, ct);

        return plans
            .Select(p => new PlanSummary(
                p.Id,
                p.CertificationId,
                names[p.CertificationId].ExamCode,
                names[p.CertificationId].Name,
                p.ExamDate,
                p.Status,
                p.Sessions.Count(s => s.Status == SessionStatus.Completed),
                p.Sessions.Count))
            .ToList();
    }

    public Task<PlanView> GetAsync(Guid planId, CancellationToken ct) =>
        MutateAsync(planId, _ => [], save: false, ct);

    public Task<PlanView> CompleteSessionAsync(Guid planId, Guid sessionId, CancellationToken ct) =>
        MutateAsync(planId, plan =>
        {
            plan.CompleteSession(sessionId, clock.Now);
            return [];
        }, save: true, ct);

    public Task<PlanView> MissSessionAsync(Guid planId, Guid sessionId, CancellationToken ct) =>
        MutateAsync(planId, plan => plan.MarkSessionMissed(sessionId, clock.Today), save: true, ct);

    /// <summary>Rolls planned sessions from past days forward. Safe to call on every visit.</summary>
    public Task<PlanView> RescheduleOverdueAsync(Guid planId, CancellationToken ct) =>
        MutateAsync(planId, plan => plan.Status == PlanStatus.Active ? plan.RescheduleOverdue(clock.Today) ?? [] : [], save: true, ct);

    public Task<PlanView> ArchiveAsync(Guid planId, CancellationToken ct) =>
        MutateAsync(planId, plan =>
        {
            plan.Archive();
            return [];
        }, save: true, ct);

    public async Task DeleteAsync(Guid planId, CancellationToken ct)
    {
        var plan = await LoadAsync(planId, ct);
        db.StudyPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
    }

    private async Task<PlanView> MutateAsync(
        Guid planId, Func<StudyPlan, IReadOnlyList<PlanWarning>> change, bool save, CancellationToken ct)
    {
        var plan = await LoadAsync(planId, ct);
        var warnings = change(plan);
        if (save)
        {
            await db.SaveChangesAsync(ct);
        }

        var certification = await certifications.LoadAsync(plan.CertificationId, ct);
        return ToView(plan, new OutlineIndex(certification), certification.ExamCode, certification.Name, warnings);
    }

    private async Task<StudyPlan> LoadAsync(Guid planId, CancellationToken ct) =>
        await db.StudyPlans.Include(p => p.Sessions).SingleOrDefaultAsync(p => p.Id == planId && p.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Study plan");

    private PlanSettings ToSettings(PlanRequest request)
    {
        if (request.StudyDays is null || request.StudyDays.Count == 0)
        {
            throw new DomainException("Pick at least one study day.");
        }

        return new PlanSettings(
            request.StartDate ?? clock.Today,
            request.ExamDate,
            request.HoursPerWeek,
            StudyDaysExtensions.FromDaysOfWeek(request.StudyDays),
            request.ReviewBufferPercent ?? PlanSettings.DefaultReviewBufferPercent);
    }

    private static PlanView ToView(
        StudyPlan plan, OutlineIndex index, string examCode, string name, IReadOnlyList<PlanWarning> warnings) => new(
        plan.Id,
        plan.CertificationId,
        examCode,
        name,
        plan.StartDate,
        plan.ExamDate,
        plan.HoursPerWeek,
        plan.StudyDays.ToDaysOfWeek(),
        plan.ReviewBufferPercent,
        plan.Status,
        plan.Shortfall,
        plan.Sessions.Select(index.ToView).ToList(),
        warnings);
}
