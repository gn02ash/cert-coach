using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Domain.Common;
using CertCoach.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CertCoach.Application.Search;

/// <summary>Hybrid retrieval over the signed-in user's documents.</summary>
public sealed class SearchService(
    ICertCoachDbContext db,
    ICurrentUser user,
    IChunkRetriever retriever,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    AiCapabilities capabilities)
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 50;

    /// <summary>How many results each retriever contributes before fusion.</summary>
    public const int CandidatesPerRetriever = 40;

    public const int MaxQueryLength = 1000;

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var query = request.Query?.Trim() ?? "";
        if (query.Length == 0)
        {
            throw new DomainException("Enter something to search for.");
        }

        if (query.Length > MaxQueryLength)
        {
            throw new DomainException($"Search for at most {MaxQueryLength} characters.");
        }

        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var candidates = Math.Max(CandidatesPerRetriever, limit);

        IReadOnlyList<ChunkHit> vector = [];
        IReadOnlyList<ChunkHit> text = [];

        if (request.Mode is SearchMode.Hybrid or SearchMode.Vector)
        {
            var queryVector = (await embeddings.GenerateVectorAsync(query, cancellationToken: ct)).ToArray();
            vector = await retriever.VectorSearchAsync(user.UserId, queryVector, request.CertificationId, candidates, ct);
        }

        if (request.Mode is SearchMode.Hybrid or SearchMode.Text)
        {
            text = await retriever.TextSearchAsync(user.UserId, query, request.CertificationId, candidates, ct);
        }

        var fused = ReciprocalRankFusion.Fuse([vector, text]).Take(limit).ToList();
        var similarity = vector.ToDictionary(h => h.ChunkId, h => h.Score);
        var hits = await HydrateAsync(fused, similarity, ct);

        return new SearchResponse(query, request.Mode, capabilities.SemanticSearch, hits);
    }

    private async Task<List<SearchHit>> HydrateAsync(
        List<ReciprocalRankFusion.Fused> fused, Dictionary<Guid, double> similarity, CancellationToken ct)
    {
        var ids = fused.Select(f => f.ChunkId).ToList();
        var chunks = await db.DocumentChunks
            .Where(c => ids.Contains(c.Id) && c.OwnerId == user.UserId)
            .Join(db.Documents, c => c.DocumentId, d => d.Id, (c, d) => new
            {
                c.Id,
                c.DocumentId,
                d.CustomTitle,
                d.ExtractedTitle,
                d.FileName,
                c.HeadingPath,
                c.PageStart,
                c.PageEnd,
                c.Text,
            })
            .ToDictionaryAsync(c => c.Id, ct);

        return fused
            .Where(f => chunks.ContainsKey(f.ChunkId))
            .Select(f =>
            {
                var c = chunks[f.ChunkId];
                return new SearchHit(
                    c.Id,
                    c.DocumentId,
                    Document.DisplayTitle(c.CustomTitle, c.ExtractedTitle, c.FileName),
                    c.HeadingPath,
                    c.PageStart,
                    c.PageEnd,
                    c.Text,
                    f.Score,
                    similarity.TryGetValue(f.ChunkId, out var s) ? s : null,
                    f.Ranks[0],
                    f.Ranks[1]);
            })
            .ToList();
    }
}
