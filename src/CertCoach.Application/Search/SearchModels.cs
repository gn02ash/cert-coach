namespace CertCoach.Application.Search;

public enum SearchMode
{
    /// <summary>Vector and full-text results merged with reciprocal rank fusion. The default.</summary>
    Hybrid,
    Vector,
    Text,
}

/// <param name="CertificationId">
/// Limits the search to documents for this certification plus documents not tied to any, such as general notes.
/// </param>
public sealed record SearchRequest(string Query, Guid? CertificationId = null, int Limit = SearchService.DefaultLimit, SearchMode Mode = SearchMode.Hybrid);

/// <summary>A chunk as returned by one retriever, best first.</summary>
/// <param name="Score">Cosine similarity for vector search, <c>ts_rank_cd</c> for full-text search.</param>
public sealed record ChunkHit(Guid ChunkId, double Score);

/// <param name="Score">The fused score: the sum of 1 / (k + rank) over the retrievers that found the chunk.</param>
/// <param name="Similarity">Cosine similarity to the query, when vector search found the chunk.</param>
/// <param name="VectorRank">1-based rank in the vector results, if it was there.</param>
/// <param name="TextRank">1-based rank in the full-text results, if it was there.</param>
public sealed record SearchHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string HeadingPath,
    int PageStart,
    int PageEnd,
    string Text,
    double Score,
    double? Similarity,
    int? VectorRank,
    int? TextRank);

/// <param name="Semantic">False in offline mode, where vectors capture shared words rather than meaning.</param>
public sealed record SearchResponse(string Query, SearchMode Mode, bool Semantic, IReadOnlyList<SearchHit> Hits);

/// <summary>The two retrievers. SQL-specific, so implemented in infrastructure.</summary>
public interface IChunkRetriever
{
    Task<IReadOnlyList<ChunkHit>> VectorSearchAsync(
        Guid ownerId, float[] queryVector, Guid? certificationId, int limit, CancellationToken ct);

    /// <summary>Matches any of the query's words (after stemming and stop-word removal), ranking chunks that match more of them higher.</summary>
    Task<IReadOnlyList<ChunkHit>> TextSearchAsync(
        Guid ownerId, string query, Guid? certificationId, int limit, CancellationToken ct);
}
