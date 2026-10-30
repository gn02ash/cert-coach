using CertCoach.Application.Abstractions;
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

    /// <param name="currentUser">When given, the context applies the owner query filters for this user.</param>
    public CertCoachDbContext CreateContext(ICurrentUser? currentUser = null)
    {
        var options = new DbContextOptionsBuilder<CertCoachDbContext>();
        options.UseCertCoachDatabase(_container.GetConnectionString());
        return new CertCoachDbContext(options.Options, currentUser);
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
