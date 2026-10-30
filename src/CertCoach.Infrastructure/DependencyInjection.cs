using CertCoach.Application.Abstractions;
using CertCoach.Application.Documents;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Infrastructure.Ai;
using CertCoach.Infrastructure.Documents;
using CertCoach.Infrastructure.Persistence;
using CertCoach.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CertCoach.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "CertCoach";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved when a context is first created, so tooling such as build-time OpenAPI
        // generation can start the host without a database.
        services.AddDbContext<CertCoachDbContext>(options => options.UseCertCoachDatabase(
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set ConnectionStrings__{ConnectionStringName}.")));
        services.AddScoped<ICertCoachDbContext>(sp => sp.GetRequiredService<CertCoachDbContext>());
        services.AddHealthChecks().AddDbContextCheck<CertCoachDbContext>("database");
        services.AddAi(configuration);
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();

        // Relative paths resolve against the working directory: the project folder under `dotnet run`, /app in Docker.
        services.AddSingleton<IFileStore>(new LocalFileStore(Path.GetFullPath(configuration["Storage:Path"] ?? "uploads")));
        services.AddSingleton<IngestionSignal>();
        services.AddSingleton<IIngestionSignal>(sp => sp.GetRequiredService<IngestionSignal>());
        if (configuration.GetValue("Ingestion:Enabled", defaultValue: true))
        {
            services.AddHostedService<IngestionWorker>();
        }

        return services;
    }

    /// <summary>Applies pending migrations. Used at startup in development and Docker.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Shared by the app, design-time tooling and tests so all three build the same model.</summary>
    public static DbContextOptionsBuilder UseCertCoachDatabase(this DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                // Outlines are three collections deep; split queries avoid a cartesian explosion.
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .UseVector())
            .UseSnakeCaseNamingConvention();
}
