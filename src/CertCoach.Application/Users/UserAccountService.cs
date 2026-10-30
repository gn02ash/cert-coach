using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Users;

/// <summary>What an identity provider tells us about the person signing in.</summary>
public sealed record ExternalIdentity(string Provider, string Subject, string DisplayName, string? Email, Guid? FixedUserId = null);

public sealed record UserProfile(Guid Id, string DisplayName, string? Email, string TimeZone);

public sealed class UserAccountService(ICertCoachDbContext db, TimeProvider time)
{
    /// <summary>Finds the user for this provider login, creating one on first sign-in.</summary>
    public async Task<User> SignInAsync(ExternalIdentity identity, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(
            u => u.Logins.Any(l => l.Provider == identity.Provider && l.Subject == identity.Subject), ct);

        if (user is null)
        {
            user = User.Create(identity.Provider, identity.Subject, identity.DisplayName, identity.Email, time.GetUtcNow(), identity.FixedUserId);
            db.Users.Add(user);
        }
        else
        {
            user.RecordLogin(identity.DisplayName, identity.Email, time.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> SetTimeZoneAsync(Guid userId, string timeZoneId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        user.SetTimeZone(timeZoneId);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public static UserProfile ToProfile(User user) => new(user.Id, user.DisplayName, user.Email, user.TimeZoneId);
}
