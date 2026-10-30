using CertCoach.Domain.Certifications;
using CertCoach.Domain.Common;

namespace CertCoach.Domain.Planning;

public enum PlanStatus
{
    Active = 0,
    Archived = 1,
}

/// <summary>
/// A study plan for one certification. Owns its sessions and keeps them consistent:
/// missed work is always rescheduled before the exam, or the plan reports a shortfall.
/// </summary>
public sealed class StudyPlan
{
    private readonly List<StudySession> _sessions = [];

    private StudyPlan()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CertificationId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly ExamDate { get; private set; }
    public decimal HoursPerWeek { get; private set; }
    public StudyDays StudyDays { get; private set; }
    public int ReviewBufferPercent { get; private set; }
    public PlanStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the remaining work no longer fits before the exam.</summary>
    public CapacityShortfall? Shortfall { get; private set; }

    public PlanSettings Settings => new(StartDate, ExamDate, HoursPerWeek, StudyDays, ReviewBufferPercent);

    public IReadOnlyList<StudySession> Sessions => _sessions.OrderBy(s => s.Sequence).ToList();

    public static (StudyPlan Plan, IReadOnlyList<PlanWarning> Warnings) Create(
        Guid ownerId, Certification certification, PlanSettings settings, DateTimeOffset now)
    {
        if (certification.OwnerId != ownerId)
        {
            throw new DomainException("The certification belongs to another user.");
        }

        var plan = new StudyPlan
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CertificationId = certification.Id,
            StartDate = settings.StartDate,
            ExamDate = settings.ExamDate,
            HoursPerWeek = settings.HoursPerWeek,
            StudyDays = settings.StudyDays,
            ReviewBufferPercent = settings.ReviewBufferPercent,
            Status = PlanStatus.Active,
            CreatedAt = now,
        };

        var result = PlanScheduler.Generate(settings, OutlineOf(certification));
        plan.Shortfall = result.Shortfall;
        plan._sessions.AddRange(result.Sessions.Select((draft, i) => new StudySession(plan.Id, i, draft)));
        return (plan, result.Warnings);
    }

    /// <summary>The certification outline in the shape the scheduler expects.</summary>
    public static IReadOnlyList<OutlineDomain> OutlineOf(Certification certification) =>
        certification.Domains
            .Select(d => new OutlineDomain(d.Id, d.Weight, d.Objectives.Select(o => o.Id).ToList()))
            .ToList();

    public void CompleteSession(Guid sessionId, DateTimeOffset now) => FindSession(sessionId).Complete(now);

    /// <summary>Marks a session missed and moves its content onto the remaining days.</summary>
    public IReadOnlyList<PlanWarning> MarkSessionMissed(Guid sessionId, DateOnly today)
    {
        var session = FindSession(sessionId);
        if (session.ScheduledDate > today)
        {
            throw new DomainException("Only today's or earlier sessions can be marked as missed.");
        }

        session.MarkMissed();
        return Reschedule(today, [session]);
    }

    /// <summary>
    /// Treats planned sessions from before today as missed and reschedules them.
    /// Returns null when nothing was overdue.
    /// </summary>
    public IReadOnlyList<PlanWarning>? RescheduleOverdue(DateOnly today) =>
        _sessions.Any(s => s.Status == SessionStatus.Planned && s.ScheduledDate < today)
            ? Reschedule(today, [])
            : null;

    private IReadOnlyList<PlanWarning> Reschedule(DateOnly today, IReadOnlyList<StudySession> newlyMissed)
    {
        EnsureActive();

        var overdue = Sessions.Where(s => s.Status == SessionStatus.Planned && s.ScheduledDate < today).ToList();
        overdue.ForEach(s => s.MarkMissed());

        var carryOver = newlyMissed.Concat(overdue).OrderBy(s => s.Sequence).Select(s => s.ToDraft()).ToList();
        var upcoming = Sessions.Where(s => s.Status == SessionStatus.Planned).ToList();
        var usedToday = _sessions.Count(s => s.ScheduledDate == today && s.Status != SessionStatus.Planned);

        var result = PlanScheduler.Reschedule(
            Settings, today, usedToday, carryOver, upcoming.Select(s => s.ToDraft()).ToList());

        upcoming.ForEach(s => _sessions.Remove(s));
        _sessions.AddRange(result.Sessions.Select(draft => new StudySession(Id, 0, draft)));
        Shortfall = result.Shortfall;
        Resequence();

        return result.Warnings;
    }

    /// <summary>Renumbers sessions by date; within a day, history comes before planned work.</summary>
    private void Resequence()
    {
        var ordered = _sessions
            .OrderBy(s => s.ScheduledDate)
            .ThenBy(s => s.Status == SessionStatus.Planned ? 1 : 0)
            .ThenBy(s => s.Sequence)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Resequence(i);
        }
    }

    private StudySession FindSession(Guid sessionId) =>
        _sessions.SingleOrDefault(s => s.Id == sessionId)
        ?? throw new DomainException("Session not found in this plan.");

    private void EnsureActive()
    {
        if (Status != PlanStatus.Active)
        {
            throw new DomainException("This plan is archived.");
        }
    }
}
