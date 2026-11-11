using System.Security.Claims;
using System.Text.Encodings.Web;
using CertCoach.Application.Abstractions;
using CertCoach.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CertCoach.Api.Auth;

/// <summary>
/// Signs in an MCP client from <c>Authorization: Bearer cc_pat_…</c>. Only the MCP endpoint accepts this scheme:
/// the browser API keeps to the session cookie, so a leaked token can do what the MCP tools allow and no more.
/// </summary>
internal sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IServiceScopeFactory scopes, TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "token";
    public const string PolicyName = "mcp";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        // A scope of its own: the request's services must not be created before the user is known.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICertCoachDbContext>();
        var identity = await ApiTokenService.AuthenticateAsync(db, header["Bearer ".Length..].Trim(), time.GetUtcNow(), Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("The token is unknown, revoked or expired.");
        }

        var claims = new[]
        {
            new Claim(AuthSetup.UserIdClaim, identity.UserId.ToString()),
            new Claim(ClaimTypes.Name, identity.DisplayName),
            new Claim(AuthSetup.TimeZoneClaim, identity.TimeZoneId),
            new Claim(AuthSetup.ProviderClaim, SchemeName),
        };
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        return Task.CompletedTask;
    }
}
