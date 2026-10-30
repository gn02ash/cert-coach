using CertCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(CertCoach.Infrastructure.Tests.PostgresFixture))]

namespace CertCoach.Infrastructure.Tests;

/// <summary>
/// One disposable PostgreSQL + pgvector container per test run, migrated with the real migrations.
/// Tests isolate themselves by creating their own owner IDs rather than truncating tables.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public CertCoachDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CertCoachDbContext>();
        options.UseCertCoachDatabase(_container.GetConnectionString());
        return new CertCoachDbContext(options.Options);
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
