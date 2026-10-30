namespace CertCoach.Domain.Documents;

/// <summary>A retrieval-sized passage of a document, with where it came from and its embedding.</summary>
public sealed class DocumentChunk
{
    private DocumentChunk()
    {
        Text = null!;
        HeadingPath = null!;
        Embedding = null!;
    }

    public DocumentChunk(
        Document document, int index, string text, string headingPath, int pageStart, int pageEnd, int tokenCount, float[] embedding)
    {
        Id = Guid.CreateVersion7();
        DocumentId = document.Id;
        OwnerId = document.OwnerId;
        Index = index;
        Text = text;
        HeadingPath = headingPath;
        PageStart = pageStart;
        PageEnd = pageEnd;
        TokenCount = tokenCount;
        Embedding = embedding;
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }

    /// <summary>Copied from the document so searches filter by owner without a join.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Position in the document, in reading order.</summary>
    public int Index { get; private set; }

    public string Text { get; private set; }

    /// <summary>The section the chunk starts in, such as "Retrieval › Hybrid search". Empty before the first heading.</summary>
    public string HeadingPath { get; private set; }

    public int PageStart { get; private set; }
    public int PageEnd { get; private set; }
    public int TokenCount { get; private set; }
    public float[] Embedding { get; private set; }
}
