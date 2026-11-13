using System.Text.Json.Serialization;
using CertCoach.Api.Auth;
using CertCoach.Api.Endpoints;
using CertCoach.Api.Infrastructure;
using CertCoach.Api.Mcp;
using CertCoach.Application;
using CertCoach.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

    // Numbers are numbers: no quoted "5" accepted, which also keeps the OpenAPI types exact.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCertCoachAuth(builder.Configuration);
builder.Services.AddCertCoachRateLimits(builder.Configuration);

// JSON requests are small; the upload endpoints raise the limit for themselves. No server banner either.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
    kestrel.AddServerHeader = false;
});

// Stateless: every MCP request stands alone, so nothing is kept in memory between calls.
builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "cert-coach", Version = "1.0.0" })
    .WithHttpTransport(transport => transport.Stateless = true)
    .WithTools<StudyTools>();

// Session cookies are encrypted with Data Protection keys; keep them across container restarts.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("CertCoach")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// Behind the web container's reverse proxy, so sign-in redirect URIs use the public scheme and host.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// After authentication, so limits are counted per learner.
app.UseRateLimiter();

if (app.Services.GetRequiredService<AuthOptions>().DevLogin.Enabled)
{
    StartupLog.DevLoginEnabled(app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();

app.MapGroup("/api")
    .RequireAuthorization()
    .RequireRateLimiting(RateLimits.Api)
    .AddEndpointFilter<CsrfHeaderFilter>()
    .MapCertificationEndpoints()
    .MapPlanEndpoints()
    .MapAiEndpoints()
    .MapDocumentEndpoints()
    .MapPracticeEndpoints()
    .MapCareerEndpoints()
    .MapResourceEndpoints()
    .MapTokenEndpoints()
    .MapSearchEndpoints()
    .MapAskEndpoints()
    .MapBillingEndpoints();

// AI assistants connect here with a personal access token; the session cookie is not accepted.
app.MapMcp("/mcp").RequireAuthorization(TokenAuthenticationHandler.PolicyName).RequireRateLimiting(RateLimits.Mcp);

await app.RunAsync();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
