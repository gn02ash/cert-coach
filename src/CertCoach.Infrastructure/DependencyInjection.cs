using CertCoach.Application.Abstractions;
using CertCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CertCoach.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "CertCoach";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set ConnectionStrings__{ConnectionStringName}.");

        services.AddDbContext<CertCoachDbContext>(options => options.UseCertCoachDatabase(connectionString));
        services.AddScoped<ICertCoachDbContext>(sp => sp.GetRequiredService<CertCoachDbContext>());
        services.AddHealthChecks().AddDbContextCheck<CertCoachDbContext>("database");

        return services;
    }

    /// <summary>Shared by the app, design-time tooling and tests so all three build the same model.</summary>
    public static DbContextOptionsBuilder UseCertCoachDatabase(this DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
