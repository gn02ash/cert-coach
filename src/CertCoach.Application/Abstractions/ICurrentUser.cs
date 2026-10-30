namespace CertCoach.Application.Abstractions;

/// <summary>The signed-in learner. Every query and command is scoped to this user.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    /// <summary>The learner's time zone, so "today" matches their calendar rather than the server's.</summary>
    TimeZoneInfo TimeZone { get; }
}
