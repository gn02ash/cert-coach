using System.Security.Cryptography;
using System.Text;
using CertCoach.Domain.Common;

namespace CertCoach.Domain.Users;

/// <summary>
/// A personal access token for connecting an AI assistant over MCP. The token itself is shown once and never
/// stored: only its SHA-256 hash is kept, which is enough to recognise it and useless for signing in. A fast
/// hash is safe here because the token is 256 random bits, not a password someone could guess.
/// </summary>
public sealed class ApiToken
{
    public const string Marker = "cc_pat_";
    public const int MaxNameLength = 60;
    public const int MaxLifetimeDays = 366;

    private ApiToken()
    {
        Name = null!;
        TokenHash = null!;
        Prefix = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the whole token.</summary>
    public string TokenHash { get; private set; }

    /// <summary>The first characters, so the learner can tell their tokens apart.</summary>
    public string Prefix { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Creates a token and returns it with its one-time plain text.</summary>
    public static (ApiToken Token, string PlainText) Issue(Guid ownerId, string name, int lifetimeDays, DateTimeOffset now)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainException($"Name the token, in at most {MaxNameLength} characters, so you can recognise it later.");
        }

        if (lifetimeDays is < 1 or > MaxLifetimeDays)
        {
            throw new DomainException($"A token lasts between 1 and {MaxLifetimeDays} days.");
        }

        var plainText = Marker + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = new ApiToken
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name,
            TokenHash = Hash(plainText),
            Prefix = plainText[..(Marker.Length + 4)],
            CreatedAt = now,
            ExpiresAt = now.AddDays(lifetimeDays),
        };
        return (token, plainText);
    }

    public static string Hash(string plainText) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>Records use, at most once every few minutes so signing in doesn't write on every request.</summary>
    public bool Touch(DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < TimeSpan.FromMinutes(5))
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }
}
