using CertCoach.Application.Ai;
using CertCoach.Application.Documents;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Domain.Documents;
using CertCoach.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CertCoach.Infrastructure.Documents;

/// <summary>Wakes the worker when a document is uploaded or retried.</summary>
internal sealed class IngestionSignal : WorkerSignal, IIngestionSignal;

/// <summary>
/// Processes pending documents one at a time. The queue is the documents table itself: a document is
/// claimed with <c>FOR UPDATE SKIP LOCKED</c>, so jobs survive restarts and two instances never take the same one.
/// </summary>
internal sealed partial class IngestionWorker(
    IServiceScopeFactory scopes, IngestionSignal signal, TimeProvider time, ILogger<IngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>A claim older than this belongs to a worker that stopped mid-document.</summary>
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var requeueChecked = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!requeueChecked)
                {
                    await RequeueOutdatedEmbeddingsAsync(stoppingToken);
                    requeueChecked = true;
                }

                while (await ClaimNextAsync(stoppingToken) is { } documentId)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<DocumentIngestionService>().ProcessAsync(documentId, stoppingToken);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // The database may be briefly unavailable; keep the worker alive and try again on the next poll.
                LogLoopFailed(e);
            }

            await signal.WaitAsync(PollInterval, stoppingToken);
        }
    }

    private async Task<Guid?> ClaimNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
        var now = time.GetUtcNow();
        var staleBefore = now - StaleClaim;

        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE documents
            SET status = 'Processing', attempts = attempts + 1, processing_started_at = {now}, next_attempt_at = NULL
            WHERE id = (
                SELECT id FROM documents
                WHERE (status = 'Pending' AND (next_attempt_at IS NULL OR next_attempt_at <= {now}))
                   OR (status = 'Processing' AND processing_started_at < {staleBefore})
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        return claimed.Count == 0 ? null : claimed[0];
    }

    /// <summary>When the configured embedding model changes, every document is processed again with it.</summary>
    private async Task RequeueOutdatedEmbeddingsAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
        var model = scope.ServiceProvider.GetRequiredService<AiCapabilities>().EmbeddingModel;

        var requeued = await db.Documents.IgnoreQueryFilters()
            .Where(d => d.Status == DocumentStatus.Ready && d.EmbeddingModel != model)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DocumentStatus.Pending)
                .SetProperty(d => d.Attempts, 0)
                .SetProperty(d => d.NextAttemptAt, (DateTimeOffset?)null), ct);

        if (requeued > 0)
        {
            LogRequeued(requeued, model);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The ingestion loop failed; retrying on the next poll")]
    private partial void LogLoopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-embedding {Count} documents with {Model}")]
    private partial void LogRequeued(int count, string model);
}
