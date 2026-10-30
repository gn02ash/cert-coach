using CertCoach.Domain.Common;

namespace CertCoach.Domain.Planning;

/// <summary>One scheduled study block of 30–45 minutes.</summary>
public sealed class StudySession
{
    private StudySession()
    {
        ObjectiveIds = [];
    }

    internal StudySession(Guid planId, int sequence, SessionDraft draft)
    {
        Id = Guid.CreateVersion7();
        PlanId = planId;
        Sequence = sequence;
        ScheduledDate = draft.Date;
        DurationMinutes = draft.DurationMinutes;
        Kind = draft.Kind;
        FocusDomainId = draft.FocusDomainId;
        ObjectiveIds = [.. draft.ObjectiveIds];
        Status = SessionStatus.Planned;
    }

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }

    /// <summary>Position in the plan, ordered by date then slot within the day.</summary>
    public int Sequence { get; private set; }

    public DateOnly ScheduledDate { get; private set; }
    public int DurationMinutes { get; private set; }
    public SessionKind Kind { get; private set; }
    public SessionStatus Status { get; private set; }

    /// <summary>The domain a Review session revisits. Null for Learn and MockExam sessions.</summary>
    public Guid? FocusDomainId { get; private set; }

    /// <summary>The objectives a Learn session covers, in study order.</summary>
    public IReadOnlyList<Guid> ObjectiveIds { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    internal void Complete(DateTimeOffset now)
    {
        EnsurePlanned();
        Status = SessionStatus.Completed;
        CompletedAt = now;
    }

    internal void MarkMissed()
    {
        EnsurePlanned();
        Status = SessionStatus.Missed;
    }

    internal void Resequence(int sequence) => Sequence = sequence;

    internal SessionDraft ToDraft() => new(ScheduledDate, DurationMinutes, Kind, ObjectiveIds, FocusDomainId);

    private void EnsurePlanned()
    {
        if (Status != SessionStatus.Planned)
        {
            throw new DomainException($"Session is already {Status.ToString().ToLowerInvariant()}.");
        }
    }
}
