namespace CertCoach.Domain.Planning;

/// <summary>Scheduler input: one exam domain with its weight and objectives in study order.</summary>
public sealed record OutlineDomain(Guid DomainId, decimal Weight, IReadOnlyList<Guid> ObjectiveIds);

/// <summary>A session the scheduler proposes, not yet persisted.</summary>
public sealed record SessionDraft(
    DateOnly Date,
    int DurationMinutes,
    SessionKind Kind,
    IReadOnlyList<Guid> ObjectiveIds,
    Guid? FocusDomainId = null);

/// <summary>How far short the plan is, with two ways to close the gap.</summary>
public sealed record CapacityShortfall(int MissingSessions, decimal SuggestedHoursPerWeek, DateOnly SuggestedExamDate);

public enum PlanWarningCode
{
    /// <summary>Not every objective gets its own session, so some sessions cover several.</summary>
    ObjectivesPacked,

    /// <summary>The requested hours exceed two 45-minute sessions on every study day.</summary>
    HoursCapped,

    /// <summary>No study days fall between the start and the exam.</summary>
    NoStudySlots,
}

public sealed record PlanWarning(PlanWarningCode Code, string Message);

public sealed record ScheduleResult(
    IReadOnlyList<SessionDraft> Sessions,
    IReadOnlyList<PlanWarning> Warnings,
    CapacityShortfall? Shortfall);
