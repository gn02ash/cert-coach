using CertCoach.Domain.Certifications;
using CertCoach.Domain.Common;

namespace CertCoach.Domain.Documents;

public enum DocumentStatus
{
    /// <summary>Waiting for the ingestion worker, possibly until <see cref="Document.NextAttemptAt"/>.</summary>
    Pending,
    Processing,
    Ready,
    Failed,
}

/// <summary>
/// An uploaded PDF. The file lives in file storage; its searchable text lives in <see cref="DocumentChunk"/> rows,
/// which are replaced as a whole each time the document is processed.
/// </summary>
public sealed class Document
{
    /// <summary>Transient failures (a provider timeout, a rate limit) are retried this many times in total.</summary>
    public const int MaxAttempts = 3;

    public const int MaxTitleLength = 200;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);

    private Document()
    {
        FileName = null!;
        ContentHash = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    /// <summary>The user's title if they renamed it, else the PDF's own title, else one made from the file name.</summary>
    public string Title => DisplayTitle(CustomTitle, ExtractedTitle, FileName);

    /// <summary>Set only by the user, so it always wins.</summary>
    public string? CustomTitle { get; private set; }

    /// <summary>
    /// From the PDF's metadata or first heading. Set only by the ingestion worker, in its own column,
    /// so a rename while the document is processing is never overwritten.
    /// </summary>
    public string? ExtractedTitle { get; private set; }

    public string FileName { get; private set; }
    public long SizeBytes { get; private set; }

    /// <summary>Hex SHA-256 of the file, used to spot the same PDF uploaded twice.</summary>
    public string ContentHash { get; private set; }

    public Guid? CertificationId { get; private set; }
    public DocumentStatus Status { get; private set; }
    public string? Error { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public int? PageCount { get; private set; }
    public int ChunkCount { get; private set; }

    /// <summary>The model the chunks were embedded with. A different configured model triggers reprocessing.</summary>
    public string? EmbeddingModel { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Where the file is kept, relative to the storage root.</summary>
    public string StorageKey => $"{OwnerId:N}/{Id:N}.pdf";

    public static Document Create(
        Guid ownerId, string fileName, long sizeBytes, string contentHash, Guid? certificationId, DateTimeOffset now)
    {
        // Browsers may send a full client path; either separator can appear whatever the server's OS.
        fileName = Certification.Required(fileName, nameof(fileName));
        fileName = Certification.Required(fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..], nameof(fileName));
        return new Document
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            FileName = fileName,
            SizeBytes = sizeBytes,
            ContentHash = Certification.Required(contentHash, nameof(contentHash)).ToLowerInvariant(),
            CertificationId = certificationId,
            Status = DocumentStatus.Pending,
            CreatedAt = now,
        };
    }

    public void Update(string title, Guid? certificationId)
    {
        title = Certification.Required(title, nameof(title));
        if (title.Length > MaxTitleLength)
        {
            throw new DomainException($"Title must be at most {MaxTitleLength} characters.");
        }

        if (title != Title)
        {
            CustomTitle = title;
        }

        CertificationId = certificationId;
    }

    /// <summary>Claimed by the ingestion worker. Usually done in SQL; this is the in-memory equivalent.</summary>
    public void StartProcessing(DateTimeOffset now)
    {
        if (Status != DocumentStatus.Pending)
        {
            throw new DomainException($"Only a pending document can be processed; this one is {Status.ToString().ToLowerInvariant()}.");
        }

        Status = DocumentStatus.Processing;
        Attempts++;
        ProcessingStartedAt = now;
        NextAttemptAt = null;
    }

    public void CompleteProcessing(int pageCount, string? extractedTitle, int chunkCount, string embeddingModel, DateTimeOffset now)
    {
        EnsureProcessing();
        Status = DocumentStatus.Ready;
        PageCount = pageCount;
        ChunkCount = chunkCount;
        EmbeddingModel = embeddingModel;
        ProcessedAt = now;
        Error = null;
        ProcessingStartedAt = null;

        var title = extractedTitle?.Trim();
        ExtractedTitle = string.IsNullOrEmpty(title) ? null : title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    /// <param name="retryable">
    /// True for failures outside the file (network, rate limits), which are retried with a growing delay.
    /// A file that cannot be read fails at once.
    /// </param>
    public void FailProcessing(string error, bool retryable, DateTimeOffset now)
    {
        EnsureProcessing();
        Error = error;
        ProcessingStartedAt = null;

        if (retryable && Attempts < MaxAttempts)
        {
            Status = DocumentStatus.Pending;
            NextAttemptAt = now + FirstRetryDelay * Math.Pow(2, Attempts - 1);
        }
        else
        {
            Status = DocumentStatus.Failed;
            NextAttemptAt = null;
        }
    }

    /// <summary>Queues the document again: after a failure, or to re-embed it with a different model.</summary>
    public void Requeue()
    {
        if (Status is DocumentStatus.Pending or DocumentStatus.Processing)
        {
            throw new DomainException("The document is already queued.");
        }

        Status = DocumentStatus.Pending;
        Attempts = 0;
        NextAttemptAt = null;
        Error = null;
    }

    private void EnsureProcessing()
    {
        if (Status != DocumentStatus.Processing)
        {
            throw new DomainException("The document is not being processed.");
        }
    }

    /// <summary>Also usable in queries that project the three columns instead of loading the entity.</summary>
    public static string DisplayTitle(string? customTitle, string? extractedTitle, string fileName) =>
        customTitle ?? extractedTitle ?? TitleFromFileName(fileName);

    private static string TitleFromFileName(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ').Trim();
        title = string.IsNullOrEmpty(title) ? fileName : title;
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }
}
