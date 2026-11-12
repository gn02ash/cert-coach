using System.Globalization;
using System.Threading.RateLimiting;
using CertCoach.Api.Auth;

namespace CertCoach.Api.Infrastructure;

/// <summary>
/// Request limits per learner (or per IP address before sign-in), so one runaway script, a guessed-token attempt
/// or a leaked token can't run up a model bill or wear down the service. The numbers are generous for a person
/// and configurable under <c>RateLimiting</c>.
/// </summary>
internal static class RateLimits
{
    /// <summary>Everything under /api.</summary>
    public const string Api = "api";

    /// <summary>Endpoints that call a model or fetch from the internet.</summary>
    public const string Costly = "costly";

    /// <summary>Sign-in attempts, by IP address.</summary>
    public const string SignIn = "sign-in";

    /// <summary>The MCP endpoint, by token owner, or by IP address when the token is refused.</summary>
    public const string Mcp = "mcp";

    public static IServiceCollection AddCertCoachRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimiting");
        var limits = new Dictionary<string, int>
        {
            [Api] = section.GetValue("ApiPerMinute", 600),
            [Costly] = section.GetValue("CostlyPerMinute", 30),
            [SignIn] = section.GetValue("SignInPerMinute", 20),
            [Mcp] = section.GetValue("McpPerMinute", 120),
        };

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            foreach (var (name, perMinute) in limits)
            {
                options.AddPolicy(name, http => RateLimitPartition.GetFixedWindowLimiter(
                    // Each policy counts separately, so reading the dashboard never uses up the model budget.
                    $"{name}:{PartitionKey(http, byUser: name != SignIn)}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            }
        });
    }

    private static string PartitionKey(HttpContext http, bool byUser) =>
        byUser && http.User.FindFirst(AuthSetup.UserIdClaim)?.Value is { Length: > 0 } userId
            ? "user:" + userId
            : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
