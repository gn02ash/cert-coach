using System.Security.Cryptography;
using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using CertCoach.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Documents;

public sealed class DocumentService(
    ICertCoachDbContext db, ICurrentUser user, UserClock clock, IFileStore files, IIngestionSignal ingestion)
{
    public const long MaxUploadBytes = 50 * 1024 * 1024;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken ct)
    {
        var documents = await db.Documents
            .Where(d => d.OwnerId == user.UserId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
        var examCodes = await ExamCodesAsync(ct);
        return documents.Select(d => ToSummary(d, examCodes)).ToList();
    }

    public async Task<DocumentSummary> UploadAsync(Stream content, string fileName, Guid? certificationId, CancellationToken ct)
    {
        if (!content.CanSeek)
        {
            var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, ct);
            buffered.Position = 0;
            content = buffered;
        }

        if (content.Length == 0)
        {
            throw new DomainException("The file is empty.");
        }

        if (content.Length > MaxUploadBytes)
        {
            throw new DomainException($"The file is larger than {MaxUploadBytes / 1024 / 1024} MB.");
        }

        if (!await StartsWithPdfSignatureAsync(content, ct))
        {
            throw new DomainException("Only PDF files can be uploaded.");
        }

        await EnsureCertificationAsync(certificationId, ct);

        content.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct));
        var existing = await db.Documents.FirstOrDefaultAsync(d => d.OwnerId == user.UserId && d.ContentHash == hash, ct);
        if (existing is not null)
        {
            throw new ConflictException($"This PDF is already in your library as \"{existing.Title}\".");
        }

        var document = Document.Create(user.UserId, fileName, content.Length, hash, certificationId, clock.Now);
        content.Position = 0;
        await files.SaveAsync(document.StorageKey, content, ct);

        db.Documents.Add(document);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Delete(document.StorageKey);
            throw;
        }

        ingestion.Notify();
        return ToSummary(document, await ExamCodesAsync(ct));
    }

    public async Task<DocumentSummary> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct)
    {
        var document = await LoadAsync(id, ct);
        await EnsureCertificationAsync(request.CertificationId, ct);
        document.Update(request.Title, request.CertificationId);
        await db.SaveChangesAsync(ct);
        return ToSummary(document, await ExamCodesAsync(ct));
    }

    /// <summary>Processes a failed document again from the start.</summary>
    public async Task<DocumentSummary> RetryAsync(Guid id, CancellationToken ct)
    {
        var document = await LoadAsync(id, ct);
        document.Requeue();
        await db.SaveChangesAsync(ct);
        ingestion.Notify();
        return ToSummary(document, await ExamCodesAsync(ct));
    }

    public async Task<DocumentFile> OpenAsync(Guid id, CancellationToken ct)
    {
        var document = await LoadAsync(id, ct);
        try
        {
            return new DocumentFile(files.OpenRead(document.StorageKey), document.FileName);
        }
        catch (FileNotFoundException)
        {
            throw new NotFoundException("Document file");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var document = await LoadAsync(id, ct);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);

        // After the rows are gone, so a failed delete never leaves a document without its file.
        files.Delete(document.StorageKey);
    }

    private async Task<Document> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Documents.SingleOrDefaultAsync(d => d.Id == id && d.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Document");

    private async Task EnsureCertificationAsync(Guid? certificationId, CancellationToken ct)
    {
        if (certificationId is { } id && !await db.Certifications.AnyAsync(c => c.Id == id && c.OwnerId == user.UserId, ct))
        {
            throw new NotFoundException("Certification");
        }
    }

    private Task<Dictionary<Guid, string>> ExamCodesAsync(CancellationToken ct) =>
        db.Certifications.Where(c => c.OwnerId == user.UserId).ToDictionaryAsync(c => c.Id, c => c.ExamCode, ct);

    private static async Task<bool> StartsWithPdfSignatureAsync(Stream content, CancellationToken ct)
    {
        content.Position = 0;
        var header = new byte[PdfSignature.Length];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return read == header.Length && header.AsSpan().SequenceEqual(PdfSignature);
    }

    internal static DocumentSummary ToSummary(Document d, IReadOnlyDictionary<Guid, string> examCodes) => new(
        d.Id,
        d.Title,
        d.FileName,
        d.SizeBytes,
        d.Status,
        d.Error,
        d.PageCount,
        d.ChunkCount,
        d.CertificationId,
        d.CertificationId is { } c ? examCodes.GetValueOrDefault(c) : null,
        d.CreatedAt,
        d.ProcessedAt);
}
