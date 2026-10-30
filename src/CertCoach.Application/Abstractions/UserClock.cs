namespace CertCoach.Application.Abstractions;

/// <summary>Current time as seen by the signed-in learner.</summary>
public sealed class UserClock(TimeProvider time, ICurrentUser user)
{
    public DateTimeOffset Now => time.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, user.TimeZone).DateTime);
}
