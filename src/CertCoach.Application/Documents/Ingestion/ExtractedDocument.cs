namespace CertCoach.Application.Documents.Ingestion;

public enum BlockKind
{
    Heading,
    Paragraph,
}

/// <summary>A heading or paragraph in reading order.</summary>
/// <param name="Page">1-based page number, which citations point to.</param>
/// <param name="HeadingLevel">1 for the largest headings; 0 for paragraphs.</param>
public sealed record DocumentBlock(int Page, BlockKind Kind, string Text, int HeadingLevel = 0);

/// <param name="Title">From the PDF metadata or the first top-level heading, when there is one.</param>
public sealed record ExtractedDocument(int PageCount, string? Title, IReadOnlyList<DocumentBlock> Blocks)
{
    /// <summary>False for scanned PDFs without a text layer, which need OCR.</summary>
    public bool HasText => Blocks.Any(b => b.Kind == BlockKind.Paragraph);
}

/// <summary>Reads the text layer of a PDF into headings and paragraphs.</summary>
public interface IPdfTextExtractor
{
    /// <exception cref="InvalidDocumentException">The file is not a readable PDF.</exception>
    ExtractedDocument Extract(Stream pdf);
}

public sealed class InvalidDocumentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Counts tokens the way the embedding model does, so chunks stay inside its input limit.</summary>
public interface ITokenCounter
{
    int Count(string text);
}
