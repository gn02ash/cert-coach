namespace CertCoach.Domain.Planning;

public enum SessionKind
{
    /// <summary>First pass over one or more objectives.</summary>
    Learn = 0,

    /// <summary>Revisit a domain near the exam. This is the buffer consumed first when sessions are missed.</summary>
    Review = 1,

    /// <summary>Timed practice across all domains, scheduled last.</summary>
    MockExam = 2,
}

public enum SessionStatus
{
    Planned = 0,
    Completed = 1,
    Missed = 2,
}
