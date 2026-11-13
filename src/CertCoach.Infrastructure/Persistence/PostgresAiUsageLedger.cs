using CertCoach.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Infrastructure.Persistence;

/// <summary>One upsert per call, so two calls finishing at once both count.</summary>
internal sealed class PostgresAiUsageLedger(CertCoachDbContext db) : IAiUsageLedger
{
    public Task AddAsync(Guid userId, DateOnly month, long units, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO ai_usage (user_id, month, units, calls) VALUES ({userId}, {month}, {units}, 1)
             ON CONFLICT (user_id, month) DO UPDATE SET units = ai_usage.units + EXCLUDED.units, calls = ai_usage.calls + 1
             """,
            ct);
}
