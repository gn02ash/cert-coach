using CertCoach.Application.Abstractions;
using CertCoach.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CertCoach.Api.Auth;

public sealed record AuthProvider(string Id, string Name);

public sealed record TimeZoneRequest(string TimeZone);

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapGet("/providers", (AuthOptions options) =>
            {
                var providers = new List<AuthProvider>();
                if (options.Microsoft.IsConfigured)
                {
                    providers.Add(new AuthProvider(AuthSetup.MicrosoftScheme, "Microsoft"));
                }

                if (options.Google.IsConfigured)
                {
                    providers.Add(new AuthProvider(AuthSetup.GoogleScheme, "Google"));
                }

                if (options.DevLogin.Enabled)
                {
                    providers.Add(new AuthProvider(AuthSetup.DevProvider, "Local dev login"));
                }

                return providers;
            })
            .WithName("ListAuthProviders");

        group.MapGet("/login/{provider}", async (string provider, string? returnUrl, HttpContext http, AuthOptions options, UserAccountService accounts) =>
            {
                var target = SafeReturnUrl(returnUrl);

                if (provider == AuthSetup.DevProvider)
                {
                    if (!options.DevLogin.Enabled)
                    {
                        return Results.NotFound();
                    }

                    var user = await accounts.SignInAsync(
                        new ExternalIdentity(AuthSetup.DevProvider, AuthSetup.DevUserId.ToString(), "Local learner", null, AuthSetup.DevUserId),
                        http.RequestAborted);
                    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthSetup.CreatePrincipal(user, AuthSetup.DevProvider));
                    return Results.LocalRedirect(target);
                }

                var configured = provider switch
                {
                    AuthSetup.MicrosoftScheme => options.Microsoft.IsConfigured,
                    AuthSetup.GoogleScheme => options.Google.IsConfigured,
                    _ => false,
                };

                return configured
                    ? Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [provider])
                    : Results.NotFound();
            })
            .WithName("Login");

        group.MapPost("/logout", async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.NoContent();
            })
            .WithName("Logout");

        group.MapGet("/me", (HttpContext http, ICurrentUser user) => new UserProfile(
                user.UserId,
                http.User.Identity?.Name ?? "Learner",
                http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                user.TimeZone.Id))
            .RequireAuthorization()
            .WithName("GetMe");

        group.MapPut("/me/time-zone", async (TimeZoneRequest request, HttpContext http, ICurrentUser current, UserAccountService accounts) =>
            {
                var user = await accounts.SetTimeZoneAsync(current.UserId, request.TimeZone, http.RequestAborted);
                var provider = http.User.FindFirst(AuthSetup.ProviderClaim)?.Value ?? AuthSetup.DevProvider;

                // Re-issue the cookie so the new time zone applies to the next request.
                await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthSetup.CreatePrincipal(user, provider));
                return UserAccountService.ToProfile(user);
            })
            .RequireAuthorization()
            .AddEndpointFilter<CsrfHeaderFilter>()
            .WithName("SetTimeZone");

        return app;
    }

    /// <summary>Only same-site paths, so the login flow cannot be used as an open redirect.</summary>
    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
