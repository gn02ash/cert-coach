using System.Text.Json.Serialization;
using CertCoach.Api.Auth;
using CertCoach.Api.Endpoints;
using CertCoach.Api.Infrastructure;
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();

app.MapGroup("/api")
    .RequireAuthorization()
    .AddEndpointFilter<CsrfHeaderFilter>()
    .MapCertificationEndpoints()
    .MapPlanEndpoints();

await app.RunAsync();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
