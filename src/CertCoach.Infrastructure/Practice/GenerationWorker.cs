using CertCoach.Application.Abstractions;
using CertCoach.Application.Practice.Generation;
using CertCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CertCoach.Infrastructure.Practice;

/// <summary>Wakes the worker when a generation run is started.</summary>
internal sealed class GenerationSignal : WorkerSignal, IGenerationSignal;

/// <summary>
/// Works through generation runs one at a time. Like ingestion, the queue is the table itself: a run is claimed
/// with <c>FOR UPDATE SKIP LOCKED</c>, and a running job whose heartbeat stopped is picked up again where it left off.
/// </summary>
internal sealed partial class GenerationWorker(
    IServiceScopeFactory scopes, GenerationSignal signal, TimeProvider time, ILogger<GenerationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>One objective takes one model call; a heartbeat older than this means the worker stopped.</summary>
    private static readonly TimeSpan StaleHeartbeat = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ClaimNextAsync(stoppingToken) is { } claimed)
                {
                    await RunAsync(claimed, stoppingToken);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                LogLoopFailed(e);
            }

            await signal.WaitAsync(PollInterval, stoppingToken);
        }
    }

    private async Task RunAsync(ClaimedJob claimed, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        // Before anything else is resolved, so the context, search and clock all act as the run's owner.
        scope.ServiceProvider.GetRequiredService<BackgroundUser>().Set(claimed.OwnerId, claimed.TimeZoneId);
        try
        {
            await scope.ServiceProvider.GetRequiredService<GenerationService>().RunAsync(claimed.Id, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Already recorded on the run; log it and move on to the next one.
            LogRunFailed(e, claimed.Id);
        }
    }

    private async Task<ClaimedJob?> ClaimNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
        var now = time.GetUtcNow();
        var staleBefore = now - StaleHeartbeat;

        var claimed = await db.Database.SqlQuery<ClaimedJob>($"""
            UPDATE generation_jobs AS j
            SET status = 'Running', started_at = COALESCE(j.started_at, {now}), heartbeat_at = {now}
            FROM users AS u
            WHERE u.id = j.owner_id AND j.id = (
                SELECT id FROM generation_jobs
                WHERE status = 'Queued' OR (status = 'Running' AND heartbeat_at < {staleBefore})
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING j.id, j.owner_id, u.time_zone_id
            """).ToListAsync(ct);

        return claimed.Count == 0 ? null : claimed[0];
    }

    internal sealed record ClaimedJob(Guid Id, Guid OwnerId, string TimeZoneId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The generation loop failed; retrying on the next poll")]
    private partial void LogLoopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation run {JobId} failed")]
    private partial void LogRunFailed(Exception exception, Guid jobId);
}
