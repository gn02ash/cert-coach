using CertCoach.Application.Abstractions;

namespace CertCoach.Api.Auth;

/// <summary>Reads the signed-in user from the session cookie's claims. No database round trip.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary><see cref="Guid.Empty"/> when nobody is signed in, which matches no data.</summary>
    public Guid UserId { get; } =
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(AuthSetup.UserIdClaim)?.Value, out var id) ? id : Guid.Empty;

    public TimeZoneInfo TimeZone { get; } =
        TimeZoneInfo.TryFindSystemTimeZoneById(accessor.HttpContext?.User.FindFirst(AuthSetup.TimeZoneClaim)?.Value ?? "UTC", out var zone)
            ? zone
            : TimeZoneInfo.Utc;
}
