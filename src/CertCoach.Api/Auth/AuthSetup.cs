using System.Security.Claims;
using CertCoach.Application.Abstractions;
using CertCoach.Application.Users;
using CertCoach.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Validators;

namespace CertCoach.Api.Auth;

/// <summary>
/// Backend-for-frontend authentication: the API runs the OAuth/OIDC flows and the React app only
/// ever holds an HttpOnly session cookie, never a token.
/// </summary>
internal static class AuthSetup
{
    public const string MicrosoftScheme = "microsoft";
    public const string GoogleScheme = "google";
    public const string DevProvider = "dev";

    public const string UserIdClaim = "cc_uid";
    public const string TimeZoneClaim = "cc_tz";
    public const string ProviderClaim = "cc_provider";

    /// <summary>The fixed user behind dev login. Matches data created before sign-in existed.</summary>
    public static readonly Guid DevUserId = new("00000000-0000-0000-0000-000000000001");

    public static IServiceCollection AddCertCoachAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        services.AddSingleton(options);

        var auth = services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "certcoach";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(14);
                cookie.SlidingExpiration = true;

                // An API answers with status codes; only the SPA decides where to navigate.
                cookie.Events.OnRedirectToLogin = context => SetStatus(context, StatusCodes.Status401Unauthorized);
                cookie.Events.OnRedirectToAccessDenied = context => SetStatus(context, StatusCodes.Status403Forbidden);
            });

        if (options.Microsoft.IsConfigured)
        {
            auth.AddOpenIdConnect(MicrosoftScheme, "Microsoft", oidc =>
            {
                oidc.Authority = $"https://login.microsoftonline.com/{options.Microsoft.TenantId}/v2.0";
                oidc.ClientId = options.Microsoft.ClientId;
                oidc.ClientSecret = options.Microsoft.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;
                oidc.CallbackPath = "/signin-oidc";
                oidc.Scope.Add("email");
                oidc.SaveTokens = false;

                // Multi-tenant authorities issue tokens per tenant; validate the issuer against
                // the token's own tenant instead of a single fixed issuer.
                oidc.TokenValidationParameters.IssuerValidator =
                    AadIssuerValidator.GetAadIssuerValidator(oidc.Authority).Validate;

                oidc.Events.OnTicketReceived = context => SignInLocalUserAsync(context, MicrosoftScheme);
            });
        }

        if (options.Google.IsConfigured)
        {
            auth.AddGoogle(GoogleScheme, "Google", google =>
            {
                google.ClientId = options.Google.ClientId!;
                google.ClientSecret = options.Google.ClientSecret!;
                google.CallbackPath = "/signin-google";
                google.Events.OnTicketReceived = context => SignInLocalUserAsync(context, GoogleScheme);
            });
        }

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }

    /// <summary>The cookie principal: our own user ID plus profile claims, no provider tokens.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user, string provider)
    {
        var claims = new List<Claim>
        {
            new(UserIdClaim, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(TimeZoneClaim, user.TimeZoneId),
            new(ProviderClaim, provider),
        };
        if (user.Email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "CertCoach", ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>Maps the provider's identity to a local user, then replaces the principal before the cookie is written.</summary>
    private static async Task SignInLocalUserAsync(TicketReceivedContext context, string provider)
    {
        var external = context.Principal ?? throw new InvalidOperationException("The identity provider returned no user.");
        var subject = external.FindFirstValue(ClaimTypes.NameIdentifier) ?? external.FindFirstValue("sub")
            ?? throw new InvalidOperationException("The identity provider returned no subject identifier.");
        var email = external.FindFirstValue(ClaimTypes.Email) ?? external.FindFirstValue("email") ?? external.FindFirstValue("preferred_username");
        var name = external.FindFirstValue("name") ?? external.FindFirstValue(ClaimTypes.Name) ?? email ?? "Learner";

        var accounts = context.HttpContext.RequestServices.GetRequiredService<UserAccountService>();
        var user = await accounts.SignInAsync(new ExternalIdentity(provider, subject, name, email), context.HttpContext.RequestAborted);

        context.Principal = CreatePrincipal(user, provider);
    }

    private static Task SetStatus(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
