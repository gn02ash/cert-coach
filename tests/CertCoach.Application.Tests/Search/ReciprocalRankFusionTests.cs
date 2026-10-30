using CertCoach.Application.Search;

namespace CertCoach.Application.Tests.Search;

public class ReciprocalRankFusionTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    private static List<ChunkHit> Ranked(params Guid[] ids) => ids.Select((id, i) => new ChunkHit(id, 1.0 - i * 0.1)).ToList();

    [Fact]
    public void A_chunk_found_by_both_retrievers_beats_one_found_by_a_single_retriever()
    {
        var fused = ReciprocalRankFusion.Fuse([Ranked(A, B), Ranked(C, B)]);

        fused[0].ChunkId.ShouldBe(B);
        fused[0].Score.ShouldBe(2.0 / 62, tolerance: 1e-12);
        fused[0].Ranks.ShouldBe([2, 2]);
    }

    [Fact]
    public void Scores_use_ranks_not_the_retrievers_own_scores()
    {
        var vector = new List<ChunkHit> { new(A, 0.99), new(B, 0.10) };
        var text = new List<ChunkHit> { new(B, 50), new(A, 0.01) };

        var fused = ReciprocalRankFusion.Fuse([vector, text]);

        fused[0].Score.ShouldBe(fused[1].Score, tolerance: 1e-12);
    }

    [Fact]
    public void Chunks_missing_from_a_list_have_no_rank_there()
    {
        var fused = ReciprocalRankFusion.Fuse([Ranked(A), Ranked(D)]);

        fused.Single(f => f.ChunkId == A).Ranks.ShouldBe([1, null]);
        fused.Single(f => f.ChunkId == D).Ranks.ShouldBe([null, 1]);
    }

    [Fact]
    public void Ties_are_ordered_deterministically()
    {
        var first = ReciprocalRankFusion.Fuse([Ranked(B, A), Ranked(A, B)]).Select(f => f.ChunkId);
        var second = ReciprocalRankFusion.Fuse([Ranked(A, B), Ranked(B, A)]).Select(f => f.ChunkId);

        first.ShouldBe(second);
    }

    [Fact]
    public void A_smaller_k_rewards_top_ranks_more()
    {
        // B is first in one list only; A is second in both.
        var lists = new IReadOnlyList<ChunkHit>[] { Ranked(B, A), Ranked(C, A) };

        ReciprocalRankFusion.Fuse(lists, k: 60)[0].ChunkId.ShouldBe(A);
        ReciprocalRankFusion.Fuse(lists, k: 0)[0].ChunkId.ShouldNotBe(A);
    }

    [Fact]
    public void Empty_lists_fuse_to_nothing()
    {
        ReciprocalRankFusion.Fuse([[], []]).ShouldBeEmpty();
    }
}
