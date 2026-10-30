using CertCoach.Application.Documents.Ingestion;

namespace CertCoach.Application.Tests.Documents;

public class ChunkerTests
{
    /// <summary>One token per word keeps the expected sizes readable.</summary>
    private sealed class WordCounter : ITokenCounter
    {
        public int Count(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static readonly ITokenCounter Words = new WordCounter();
    private static readonly ChunkingOptions Small = new(TargetTokens: 20, OverlapTokens: 6);

    private static DocumentBlock H(int level, string text, int page = 1) => new(page, BlockKind.Heading, text, level);
    private static DocumentBlock P(string text, int page = 1) => new(page, BlockKind.Paragraph, text);

    /// <summary>A sentence of exactly <paramref name="words"/> words.</summary>
    private static string Sentence(string label, int words) =>
        string.Join(' ', Enumerable.Range(1, words).Select(i => i == 1 ? label : "word")) + ".";

    [Fact]
    public void Short_paragraphs_under_one_heading_form_one_chunk()
    {
        var chunks = Chunker.Split([H(1, "Retrieval"), P("Alpha one."), P("Beta two.")], Words, Small);

        var chunk = chunks.ShouldHaveSingleItem();
        chunk.Text.ShouldBe("Alpha one.\n\nBeta two.");
        chunk.HeadingPath.ShouldBe("Retrieval");
        chunk.TokenCount.ShouldBe(4);
    }

    [Fact]
    public void A_top_level_heading_always_starts_a_new_chunk()
    {
        var chunks = Chunker.Split([H(1, "Plan"), P("Short."), H(1, "Build"), P("Also short.")], Words, Small);

        chunks.Select(c => (c.HeadingPath, c.Text)).ShouldBe([("Plan", "Short."), ("Build", "Also short.")]);
    }

    [Fact]
    public void A_subheading_starts_a_new_chunk_only_once_the_chunk_is_half_full()
    {
        var chunks = Chunker.Split(
        [
            H(1, "Plan"),
            P("Tiny."),
            H(2, "Costs"),
            P(Sentence("Costs", 10)),
            H(2, "Quotas"),
            P("Quota text."),
        ], Words, Small);

        chunks.Count.ShouldBe(2);
        chunks[0].HeadingPath.ShouldBe("Plan");
        chunks[0].Text.ShouldBe($"Tiny.\n\nCosts\n\n{Sentence("Costs", 10)}");
        chunks[1].HeadingPath.ShouldBe("Plan › Quotas");
    }

    [Fact]
    public void Heading_path_tracks_nesting_and_resets_deeper_levels()
    {
        var chunks = Chunker.Split(
        [
            H(1, "A"), H(2, "B"), H(3, "C"), P(Sentence("one", 12)),
            H(2, "D"), P("Under D."),
        ], Words, Small);

        chunks.Select(c => c.HeadingPath).ShouldBe(["A › B › C", "A › D"]);
    }

    [Fact]
    public void Chunks_close_before_the_target_size_and_repeat_trailing_sentences()
    {
        var chunks = Chunker.Split(
        [
            P($"{Sentence("First", 8)} {Sentence("Second", 8)} {Sentence("Third", 4)}"),
            P(Sentence("Fourth", 8)),
        ], Words, Small);

        chunks.Count.ShouldBe(2);
        chunks.ShouldAllBe(c => c.TokenCount <= Small.TargetTokens + Small.OverlapTokens);
        chunks[0].TokenCount.ShouldBe(20);
        chunks[1].Text.ShouldBe($"{Sentence("Third", 4)}\n\n{Sentence("Fourth", 8)}");
    }

    [Fact]
    public void An_oversized_paragraph_is_split_by_sentence()
    {
        var text = string.Join(' ', Enumerable.Range(1, 5).Select(i => Sentence($"S{i}", 9)));

        var chunks = Chunker.Split([P(text)], Words, Small with { OverlapTokens = 0 });

        chunks.Select(c => c.TokenCount).ShouldBe([18, 18, 9]);
        chunks.ShouldAllBe(c => !c.Text.Contains('\n'));
    }

    [Fact]
    public void A_sentence_longer_than_the_target_is_split_by_words()
    {
        var chunks = Chunker.Split([P(Sentence("Long", 50))], Words, Small with { OverlapTokens = 0 });

        chunks.Select(c => c.TokenCount).ShouldBe([20, 20, 10]);
        string.Join(' ', chunks.Select(c => c.Text)).ShouldBe(Sentence("Long", 50));
    }

    [Fact]
    public void Pages_span_the_pieces_in_the_chunk()
    {
        var chunks = Chunker.Split([P("On page two.", page: 2), P("On page three.", page: 3)], Words, Small);

        var chunk = chunks.ShouldHaveSingleItem();
        (chunk.PageStart, chunk.PageEnd).ShouldBe((2, 3));
    }

    [Fact]
    public void Overlap_alone_never_becomes_a_chunk()
    {
        var chunks = Chunker.Split(
        [
            P($"{Sentence("First", 10)} {Sentence("Second", 10)}"),
            H(1, "Next"),
            P("Fresh."),
        ], Words, Small);

        chunks.Select(c => c.Text).ShouldBe([Sentence("First", 10) + " " + Sentence("Second", 10), "Fresh."]);
    }

    [Fact]
    public void Headings_without_text_produce_no_chunks()
    {
        Chunker.Split([H(1, "Only"), H(2, "Headings")], Words, Small).ShouldBeEmpty();
    }

    [Fact]
    public void Chunks_are_numbered_in_order()
    {
        var chunks = Chunker.Split([H(1, "A"), P("a."), H(1, "B"), P("b."), H(1, "C"), P("c.")], Words, Small);

        chunks.Select(c => c.Index).ShouldBe([0, 1, 2]);
    }

    [Theory]
    [InlineData("One. Two! Three? Four.", 4)]
    [InlineData("Use e.g. caching. Then measure.", 2)]
    [InlineData("Version 2.5 is out. It is fast.", 2)]
    [InlineData("He said \"Stop.\" Then left.", 2)]
    public void Splits_sentences_without_breaking_abbreviations_or_numbers(string text, int count)
    {
        Chunker.Sentences(text).Length.ShouldBe(count);
    }
}
