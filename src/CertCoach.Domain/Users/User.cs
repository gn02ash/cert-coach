using CertCoach.Domain.Common;

namespace CertCoach.Domain.Users;

/// <summary>
/// A learner. Identified by one or more external logins (Microsoft, Google), never by email:
/// matching accounts on an unverified email claim is a known account-takeover risk.
/// </summary>
public sealed class User
{
    private readonly List<ExternalLogin> _logins = [];

    private User()
    {
        DisplayName = null!;
        TimeZoneId = null!;
    }

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; }
    public string? Email { get; private set; }

    /// <summary>An IANA or Windows time zone ID, used to decide what "today" means for this learner.</summary>
    public string TimeZoneId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastLoginAt { get; private set; }

    public IReadOnlyList<ExternalLogin> Logins => _logins;

    public static User Create(
        string provider, string subject, string displayName, string? email, DateTimeOffset now, Guid? id = null)
    {
        var user = new User
        {
            Id = id ?? Guid.CreateVersion7(),
            TimeZoneId = "UTC",
            CreatedAt = now,
        };
        user._logins.Add(new ExternalLogin(provider, subject));
        user.RecordLogin(displayName, email, now);
        return user;
    }

    /// <summary>Refreshes the profile from the identity provider on each sign-in.</summary>
    public void RecordLogin(string displayName, string? email, DateTimeOffset now)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Learner" : displayName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        LastLoginAt = now;
    }

    public void SetTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            throw new DomainException($"Unknown time zone '{timeZoneId}'.");
        }

        TimeZoneId = timeZoneId;
    }
}

/// <summary>A provider-specific stable user ID, such as the OpenID Connect <c>sub</c> claim.</summary>
public sealed record ExternalLogin(string Provider, string Subject);
