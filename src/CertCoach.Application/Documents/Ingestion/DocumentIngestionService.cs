using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CertCoach.Application.Documents.Ingestion;

/// <summary>
/// Turns a claimed document into searchable chunks: extract text, chunk it, embed the chunks, and replace
/// the document's previous chunks in one transaction. Runs in the background worker, outside any user's
/// session, so it reads across owners on purpose.
/// </summary>
public sealed partial class DocumentIngestionService(
    ICertCoachDbContext db,
    IFileStore files,
    IPdfTextExtractor extractor,
    ITokenCounter tokens,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    AiCapabilities capabilities,
    TimeProvider time,
    ILogger<DocumentIngestionService> logger)
{
    /// <summary>Inputs per embeddings request; well under provider limits for ~450-token chunks.</summary>
    public const int EmbeddingBatchSize = 64;

    private const int MaxErrorLength = 500;

    /// <summary>Processes a document the worker has claimed. Anything else is left alone.</summary>
    public async Task ProcessAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.IgnoreQueryFilters().SingleOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is not { Status: DocumentStatus.Processing })
        {
            return;
        }

        try
        {
            await IngestAsync(document, ct);
            LogReady(document.Id, document.ChunkCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: the document stays claimed and is picked up again once the claim goes stale.
            throw;
        }
        catch (Exception e)
        {
            var (message, retryable) = Describe(e);
            LogFailed(e, document.Id, retryable);

            db.ChangeTracker.Clear();
            document = await db.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == documentId, ct);
            document.FailProcessing(message, retryable, time.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task IngestAsync(Document document, CancellationToken ct)
    {
        if (document.Attempts > Document.MaxAttempts)
        {
            throw new IngestionException("Processing stopped before finishing several times.", retryable: false);
        }

        ExtractedDocument extracted;
        await using (var pdf = files.OpenRead(document.StorageKey))
        {
            extracted = extractor.Extract(pdf);
        }

        if (!extracted.HasText)
        {
            throw new IngestionException(
                "No text found. This looks like a scanned PDF, which needs OCR; that isn't supported yet.", retryable: false);
        }

        var chunks = Chunker.Split(extracted.Blocks, tokens);
        var title = document.HasCustomTitle ? document.Title : extracted.Title ?? document.Title;
        var vectors = await EmbedAsync(chunks.Select(c => EmbeddingText(title, c.HeadingPath, c.Text)).ToList(), ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.DocumentChunks.IgnoreQueryFilters().Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(ct);
        db.DocumentChunks.AddRange(chunks.Select((c, i) =>
            new DocumentChunk(document, c.Index, c.Text, c.HeadingPath, c.PageStart, c.PageEnd, c.TokenCount, vectors[i])));
        document.CompleteProcessing(extracted.PageCount, extracted.Title, chunks.Count, capabilities.EmbeddingModel, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// What gets embedded: the chunk prefixed with its document title and section, so a passage that only
    /// says "it" or "this service" still matches questions about its topic.
    /// </summary>
    public static string EmbeddingText(string title, string headingPath, string text) =>
        string.IsNullOrEmpty(headingPath) ? $"{title}\n\n{text}" : $"{title}\n{headingPath}\n\n{text}";

    private async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var vectors = new List<float[]>(inputs.Count);
        foreach (var batch in inputs.Chunk(EmbeddingBatchSize))
        {
            var generated = await embeddings.GenerateAsync(batch, cancellationToken: ct);
            foreach (var embedding in generated)
            {
                if (embedding.Vector.Length != AiCapabilities.EmbeddingDimensions)
                {
                    throw new IngestionException(
                        $"The embedding model returned {embedding.Vector.Length} values instead of {AiCapabilities.EmbeddingDimensions}. " +
                        "Use a text-embedding-3 model, or a model that returns that size.",
                        retryable: false);
                }

                vectors.Add(embedding.Vector.ToArray());
            }
        }

        return vectors;
    }

    private static (string Message, bool Retryable) Describe(Exception e)
    {
        var (message, retryable) = e switch
        {
            IngestionException ie => (ie.Message, ie.Retryable),
            InvalidDocumentException ide => (ide.Message, false),
            FileNotFoundException or DirectoryNotFoundException => ("The uploaded file is missing from storage.", false),

            // Network errors, timeouts and rate limits from the embeddings provider are worth another try.
            _ => ($"Embedding failed: {e.Message}", true),
        };

        return (message.Length > MaxErrorLength ? message[..MaxErrorLength] : message, retryable);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} is ready with {ChunkCount} chunks")]
    private partial void LogReady(Guid documentId, int chunkCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processing document {DocumentId} failed (will retry: {Retryable})")]
    private partial void LogFailed(Exception exception, Guid documentId, bool retryable);
}

public sealed class IngestionException(string message, bool retryable) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}
