namespace CertCoach.Application.Abstractions;

/// <summary>
/// The learner a background job runs for. A worker sets it at the start of the job's scope, before resolving any
/// service, and the host's <see cref="ICurrentUser"/> registration then returns it instead of the HTTP user.
/// </summary>
public sealed class BackgroundUser : ICurrentUser
{
    public Guid UserId { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

    public bool IsSet { get; private set; }

    public void Set(Guid userId, string? timeZoneId)
    {
        UserId = userId;
        TimeZone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId ?? "UTC", out var zone) ? zone : TimeZoneInfo.Utc;
        IsSet = true;
    }
}
