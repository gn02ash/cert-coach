using System.Text.Json;
using System.Text.Json.Serialization;
using CertCoach.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(CertCoach.Api.IntegrationTests.CertCoachApiFactory))]

namespace CertCoach.Api.IntegrationTests;

/// <summary>
/// The real API against a disposable PostgreSQL + pgvector container, with a fixed clock.
/// Each test signs in as its own user through the <see cref="UserHeader"/> header.
/// </summary>
public sealed class CertCoachApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string UserHeader = "X-Test-User";

    /// <summary>Monday 2 November 2026, 08:00 UTC.</summary>
    public static readonly DateTimeOffset StartTime = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    /// <summary>Shared by all tests; tests that move it live in one class, so they run sequentially.</summary>
    public FakeTimeProvider Clock { get; } = new(StartTime);

    /// <summary>A client acting as a fresh user, so tests never see each other's data.</summary>
    public HttpClient CreateUserClient(Guid? userId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(UserHeader, (userId ?? Guid.NewGuid()).ToString());
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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:CertCoach", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HeaderCurrentUser>();
        });
    }

    private sealed class HeaderCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        public Guid UserId { get; } =
            Guid.TryParse(accessor.HttpContext?.Request.Headers[UserHeader], out var id) ? id : Guid.Empty;

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }
}
