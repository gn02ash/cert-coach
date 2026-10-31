using CertCoach.Application.Abstractions;
using CertCoach.Application.Plans;
using CertCoach.Application.Practice;
using CertCoach.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Dashboard;

/// <param name="ReviewDue">Practice items due today across all certifications, plus new ones today's limit allows.</param>
public sealed record DashboardView(DateOnly Today, IReadOnlyList<PlanDashboard> Plans, int ReviewDue);

public sealed record PlanDashboard(
    Guid PlanId,
    Guid CertificationId,
    string ExamCode,
    string CertificationName,
    DateOnly ExamDate,
    int DaysUntilExam,
    IReadOnlyList<SessionView> Today,
    IReadOnlyList<SessionView> Upcoming,
    PlanProgress Progress,
    IReadOnlyList<DomainProgress> Domains,
    int OverdueSessions,
    CapacityShortfall? Shortfall);

public sealed record PlanProgress(int Completed, int Missed, int Remaining, int Total, int PercentComplete);

/// <summary>How many of a domain's objectives have had at least one completed Learn session.</summary>
public sealed record DomainProgress(Guid DomainId, string Name, decimal Weight, int ObjectivesStudied, int ObjectivesTotal);

public sealed class DashboardService(ICertCoachDbContext db, ICurrentUser user, UserClock clock, ReviewService review)
{
    public const int UpcomingDays = 7;

    public async Task<DashboardView> GetAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var plans = await db.StudyPlans
            .Include(p => p.Sessions)
            .Where(p => p.OwnerId == user.UserId && p.Status == PlanStatus.Active)
            .OrderBy(p => p.ExamDate)
            .ToListAsync(ct);

        var certificationIds = plans.Select(p => p.CertificationId).ToList();
        var certifications = await db.Certifications
            .Include(c => c.Domains).ThenInclude(d => d.SkillAreas).ThenInclude(s => s.Objectives)
            .Where(c => c.OwnerId == user.UserId && certificationIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var reviewDue = await review.CountForTodayAsync(ct);
        return new DashboardView(today, plans.Select(plan =>
        {
            var certification = certifications[plan.CertificationId];
            var index = new OutlineIndex(certification);
            var sessions = plan.Sessions;

            var studied = sessions
                .Where(s => s.Kind == SessionKind.Learn && s.Status == SessionStatus.Completed)
                .SelectMany(s => s.ObjectiveIds)
                .ToHashSet();

            var completed = sessions.Count(s => s.Status == SessionStatus.Completed);
            var missed = sessions.Count(s => s.Status == SessionStatus.Missed);
            var total = completed + sessions.Count(s => s.Status == SessionStatus.Planned);

            return new PlanDashboard(
                plan.Id,
                certification.Id,
                certification.ExamCode,
                certification.Name,
                plan.ExamDate,
                plan.ExamDate.DayNumber - today.DayNumber,
                sessions.Where(s => s.ScheduledDate == today).Select(index.ToView).ToList(),
                sessions
                    .Where(s => s.Status == SessionStatus.Planned && s.ScheduledDate > today && s.ScheduledDate <= today.AddDays(UpcomingDays))
                    .Select(index.ToView)
                    .ToList(),
                new PlanProgress(completed, missed, total - completed, total, total == 0 ? 0 : (int)Math.Round(100.0 * completed / total)),
                certification.Domains
                    .Select(d => new DomainProgress(
                        d.Id,
                        d.Name,
                        d.Weight,
                        d.Objectives.Count(o => studied.Contains(o.Id)),
                        d.Objectives.Count()))
                    .ToList(),
                sessions.Count(s => s.Status == SessionStatus.Planned && s.ScheduledDate < today),
                plan.Shortfall);
        }).ToList(), reviewDue);
    }
}
