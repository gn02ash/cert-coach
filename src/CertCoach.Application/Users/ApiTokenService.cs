using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Users;

public sealed record ApiTokenView(
    Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt, bool Active);

/// <param name="Token">The token itself. Shown this once; it can't be read again.</param>
public sealed record CreatedApiToken(ApiTokenView Details, string Token);

public sealed record CreateApiTokenRequest(string Name, int LifetimeDays = 90);

/// <summary>The learner a token signs in as.</summary>
public sealed record TokenIdentity(Guid UserId, string DisplayName, string TimeZoneId);

/// <summary>Personal access tokens: created and revoked by the signed-in learner, and checked when an MCP client connects.</summary>
public sealed class ApiTokenService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    public const int MaxActiveTokens = 10;

    public async Task<IReadOnlyList<ApiTokenView>> ListAsync(CancellationToken ct) =>
        (await db.ApiTokens.Where(t => t.OwnerId == user.UserId && t.RevokedAt == null).OrderByDescending(t => t.CreatedAt).ToListAsync(ct))
        .Select(ToView)
        .ToList();

    public async Task<CreatedApiToken> CreateAsync(CreateApiTokenRequest request, CancellationToken ct)
    {
        var now = clock.Now;
        if (await db.ApiTokens.CountAsync(t => t.OwnerId == user.UserId && t.RevokedAt == null && t.ExpiresAt > now, ct) >= MaxActiveTokens)
        {
            throw new DomainException($"You have {MaxActiveTokens} active tokens. Revoke one you no longer use first.");
        }

        var (token, plainText) = ApiToken.Issue(user.UserId, request.Name, request.LifetimeDays, now);
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return new CreatedApiToken(ToView(token), plainText);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var token = await db.ApiTokens.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == user.UserId, ct)
            ?? throw new NotFoundException("Token");
        token.Revoke(clock.Now);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Who a presented token belongs to, or null if it is unknown, revoked or expired. Runs before anyone is
    /// signed in, so it looks across all learners by the token's hash.
    /// </summary>
    public static async Task<TokenIdentity?> AuthenticateAsync(ICertCoachDbContext db, string presented, DateTimeOffset now, CancellationToken ct)
    {
        if (!presented.StartsWith(ApiToken.Marker, StringComparison.Ordinal) || presented.Length > 100)
        {
            return null;
        }

        var hash = ApiToken.Hash(presented);
        var token = await db.ApiTokens.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || !token.IsActive(now))
        {
            return null;
        }

        var owner = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == token.OwnerId, ct);
        if (owner is null)
        {
            return null;
        }

        if (token.Touch(now))
        {
            await db.SaveChangesAsync(ct);
        }

        return new TokenIdentity(owner.Id, owner.DisplayName, owner.TimeZoneId);
    }

    private ApiTokenView ToView(ApiToken t) =>
        new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.ExpiresAt, t.LastUsedAt, t.IsActive(clock.Now));
}
