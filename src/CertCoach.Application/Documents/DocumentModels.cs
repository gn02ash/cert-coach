using CertCoach.Domain.Documents;

namespace CertCoach.Application.Documents;

public sealed record DocumentSummary(
    Guid Id,
    string Title,
    string FileName,
    long SizeBytes,
    DocumentStatus Status,
    string? Error,
    int? PageCount,
    int ChunkCount,
    Guid? CertificationId,
    string? ExamCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);

public sealed record UpdateDocumentRequest(string Title, Guid? CertificationId);

/// <summary>An open PDF for download; the caller disposes the stream.</summary>
public sealed record DocumentFile(Stream Content, string FileName);
