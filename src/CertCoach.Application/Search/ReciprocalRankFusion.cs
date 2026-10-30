namespace CertCoach.Application.Search;

/// <summary>
/// Merges ranked lists by rank alone, so a cosine similarity and a full-text rank never have to be put on
/// one scale. A chunk scores 1 / (k + rank) in each list it appears in; k dampens the advantage of the top spots.
/// </summary>
public static class ReciprocalRankFusion
{
    /// <summary>The value from the original paper (Cormack et al., 2009), and a common default.</summary>
    public const int DefaultK = 60;

    public sealed record Fused(Guid ChunkId, double Score, IReadOnlyList<int?> Ranks);

    /// <returns>Every chunk from any list, best first, with its 1-based rank in each input list (null when absent).</returns>
    public static IReadOnlyList<Fused> Fuse(IReadOnlyList<IReadOnlyList<ChunkHit>> lists, int k = DefaultK)
    {
        var ranks = new Dictionary<Guid, int?[]>();
        for (var list = 0; list < lists.Count; list++)
        {
            for (var i = 0; i < lists[list].Count; i++)
            {
                var id = lists[list][i].ChunkId;
                if (!ranks.TryGetValue(id, out var chunkRanks))
                {
                    ranks[id] = chunkRanks = new int?[lists.Count];
                }

                // A retriever should not return a chunk twice; if it does, its best rank counts.
                chunkRanks[list] ??= i + 1;
            }
        }

        return ranks
            .Select(r => new Fused(r.Key, r.Value.Sum(rank => rank is { } n ? 1.0 / (k + n) : 0), r.Value))
            .OrderByDescending(f => f.Score)
            // Ties (same ranks in mirrored lists) fall back to the best single rank, then the ID for stability.
            .ThenBy(f => f.Ranks.Min(rank => rank ?? int.MaxValue))
            .ThenBy(f => f.ChunkId)
            .ToList();
    }
}
