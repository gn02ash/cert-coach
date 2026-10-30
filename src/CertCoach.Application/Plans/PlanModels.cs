using CertCoach.Domain.Planning;

namespace CertCoach.Application.Plans;

public sealed record PlanRequest(
    Guid CertificationId,
    DateOnly? StartDate,
    DateOnly ExamDate,
    decimal HoursPerWeek,
    IReadOnlyList<DayOfWeek> StudyDays,
    int? ReviewBufferPercent);

public sealed record SessionObjectiveView(Guid Id, string Text, string SkillArea, Guid DomainId, string Domain);

public sealed record SessionView(
    Guid? Id,
    int Sequence,
    DateOnly Date,
    int DurationMinutes,
    SessionKind Kind,
    SessionStatus Status,
    string Title,
    Guid? DomainId,
    string? DomainName,
    IReadOnlyList<SessionObjectiveView> Objectives,
    DateTimeOffset? CompletedAt);

public sealed record PlanPreview(
    int SessionMinutes,
    int SessionsPerWeek,
    IReadOnlyList<SessionView> Sessions,
    IReadOnlyList<PlanWarning> Warnings,
    CapacityShortfall? Shortfall);

public sealed record PlanView(
    Guid Id,
    Guid CertificationId,
    string ExamCode,
    string CertificationName,
    DateOnly StartDate,
    DateOnly ExamDate,
    decimal HoursPerWeek,
    IReadOnlyList<DayOfWeek> StudyDays,
    int ReviewBufferPercent,
    PlanStatus Status,
    CapacityShortfall? Shortfall,
    IReadOnlyList<SessionView> Sessions,
    IReadOnlyList<PlanWarning> Warnings);

public sealed record PlanSummary(
    Guid Id,
    Guid CertificationId,
    string ExamCode,
    string CertificationName,
    DateOnly ExamDate,
    PlanStatus Status,
    int CompletedSessions,
    int TotalSessions);
