namespace CertCoach.Api.Auth;

/// <summary>Bound from the <c>Auth</c> configuration section. Secrets come from environment variables only.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public ExternalProviderOptions Microsoft { get; init; } = new();

    public ExternalProviderOptions Google { get; init; } = new();

    public DevLoginOptions DevLogin { get; init; } = new();
}

public sealed class ExternalProviderOptions
{
    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    /// <summary>Entra only. <c>common</c> accepts work, school and personal Microsoft accounts.</summary>
    public string TenantId { get; init; } = "common";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// One-click local sign-in so the app runs without any identity provider set up.
/// Off by default; never enable it on a machine reachable by others.
/// </summary>
public sealed class DevLoginOptions
{
    public bool Enabled { get; init; }
}
