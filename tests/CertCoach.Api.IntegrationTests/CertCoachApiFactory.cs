using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CertCoach.Domain.Users;
using CertCoach.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(CertCoach.Api.IntegrationTests.CertCoachApiFactory))]

namespace CertCoach.Api.IntegrationTests;

/// <summary>
/// The real API against a disposable PostgreSQL + pgvector container, with a fixed clock.
/// Requests carrying <see cref="UserHeader"/> are authenticated as that user; all others go
/// through the app's real cookie authentication.
/// </summary>
public sealed class CertCoachApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string UserHeader = "X-Test-User";
    public const string CsrfHeader = "X-CSRF";

    /// <summary>Monday 2 November 2026, 08:00 UTC.</summary>
    public static readonly DateTimeOffset StartTime = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "certcoach-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Fixed for the whole run; tests needing past dates use past start dates instead of moving time.</summary>
    public FakeTimeProvider Clock { get; } = new(StartTime);

    /// <summary>A client signed in as a brand-new user, so tests never see each other's data.</summary>
    public async Task<HttpClient> CreateUserClientAsync()
    {
        var userId = Guid.NewGuid();
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
            db.Users.Add(User.Create("test", userId.ToString(), "Test learner", null, StartTime, userId));
            await db.SaveChangesAsync();
        }

        var client = CreateClient();
        client.DefaultRequestHeaders.Add(UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(CsrfHeader, "1");
        return client;
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Server; // Start the host, which applies migrations.
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_uploads))
        {
            Directory.Delete(_uploads, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:CertCoach", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Auth:DevLogin:Enabled", "true");
        builder.UseSetting("Storage:Path", _uploads);

        // Test PDFs have a sentence or two per section; small chunks keep sections apart as real ones would be.
        builder.UseSetting("Ingestion:Chunking:TargetTokens", "28");
        builder.UseSetting("Ingestion:Chunking:OverlapTokens", "0");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(TestUserHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(options => options.DefaultScheme = TestUserHandler.SchemeName);
        });
    }

    /// <summary>Authenticates from the test header, or defers to the real session cookie.</summary>
    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId))
            {
                return await Context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            var identity = new ClaimsIdentity(
                [new Claim("cc_uid", userId.ToString()), new Claim("cc_tz", "UTC"), new Claim(ClaimTypes.Name, "Test learner")],
                SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
    }
}
