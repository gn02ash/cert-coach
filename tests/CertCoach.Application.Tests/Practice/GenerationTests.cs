using CertCoach.Application.Ask;
using CertCoach.Application.Practice.Generation;
using CertCoach.Domain.Practice;

namespace CertCoach.Application.Tests.Practice;

public class ClozeWriterTests
{
    private static readonly ObjectiveContext Objective = new(
        Guid.NewGuid(), Guid.NewGuid(), "DEMO-101", "Contoso AI Solutions Engineer", "RAG", "Search",
        "Combine vector and keyword search", []);

    private static readonly AskSource[] Passages =
    [
        new(1, Guid.NewGuid(), Guid.NewGuid(), "RAG notes", "Hybrid search", 3, 3,
            "Hybrid search runs vector search and keyword search in parallel. Reciprocal rank fusion merges both rankings into one list. " +
            "HNSW indexes make approximate nearest neighbour search fast on large collections."),
        new(2, Guid.NewGuid(), Guid.NewGuid(), "RAG notes", "Keyword search", 4, 4,
            "PostgreSQL full-text search ranks documents with ts_rank_cd over tsvector columns. " +
            "BM25 weighs rare terms more heavily than common ones when ranking keyword matches. Short."),
    ];

    [Fact]
    public void Blanks_a_distinctive_term_the_objective_does_not_give_away_and_cites_its_sentence()
    {
        var drafts = ClozeWriter.Write(Objective, Passages, 4);

        drafts.Count.ShouldBe(4);
        drafts.Count(d => d.Kind == PracticeItemKind.Flashcard).ShouldBe(ItemMix.For(4).Flashcards);
        foreach (var draft in drafts)
        {
            draft.Prompt.ShouldContain(ClozeWriter.Blank);
            draft.SourceNumbers.ShouldHaveSingleItem();
            var quote = draft.Quote.ShouldNotBeNull();
            Passages[draft.SourceNumbers[0] - 1].Text.ShouldContain(quote);

            // "vector", "keyword" and "search" are in the objective, so blanking them would give the answer away.
            var answer = draft.Kind == PracticeItemKind.Flashcard ? draft.Answer.Split('\n')[0] : draft.Options[draft.CorrectOptions[0]];
            answer.ShouldNotBeOneOf("vector", "keyword", "search", "Hybrid");
            quote.ShouldContain(answer);
        }
    }

    [Fact]
    public void Questions_have_four_distinct_options_of_the_same_shape()
    {
        var question = ClozeWriter.Write(Objective, Passages, 5).First(d => d.Kind == PracticeItemKind.SingleChoice);

        question.Options.Count.ShouldBe(4);
        question.Options.Distinct().Count().ShouldBe(4);
        question.CorrectOptions.ShouldHaveSingleItem();
        question.Prompt.ShouldStartWith("Fill in the blank: ");
    }

    [Fact]
    public void Is_deterministic_and_skips_items_the_objective_already_has()
    {
        var first = ClozeWriter.Write(Objective, Passages, 3);
        var again = ClozeWriter.Write(Objective, Passages, 3);
        var next = ClozeWriter.Write(Objective with { ExistingPrompts = [first[0].Prompt] }, Passages, 3);

        again.Select(d => (d.Prompt, string.Join('|', d.Options))).ShouldBe(first.Select(d => (d.Prompt, string.Join('|', d.Options))));
        next.Select(d => d.Prompt).ShouldNotContain(first[0].Prompt);
    }

    [Fact]
    public void Never_blanks_half_of_a_hyphenated_code()
    {
        AskSource codes = new(1, Guid.NewGuid(), Guid.NewGuid(), "Exams", "", 1, 1,
            "XYZ-200 is a back-end engineering exam for Contoso workloads. Candidates for AB-900 manage tenant-wide governance policies daily. " +
            "QRS-410 measures data-pipeline design skills on distributed platforms.");

        var drafts = ClozeWriter.Write(Objective, [codes], 6);

        drafts.ShouldNotBeEmpty();
        drafts.ShouldAllBe(d => !d.Prompt.Contains("_____-") && !d.Prompt.Contains("-_____"));
    }

    [Fact]
    public void Skips_labels_cheat_sheet_arrows_and_fragments_and_keeps_dotted_names_whole()
    {
        AskSource notes = new(1, Guid.NewGuid(), Guid.NewGuid(), "Notes", "", 1, 1,
            "EXAM SIGNAL Prefer the managed service whenever it meets every stated requirement. " +
            "ANSWER: Container Apps with a Service Bus scaler handles the load. " +
            "\"Sub-millisecond cache\" → Redis for the hot keys only. " +
            "Azure Search; still validate recall. " +
            "The processor library is available for .NET and Java applications that need checkpoints.");

        var drafts = ClozeWriter.Write(Objective, [notes], 5);

        drafts.ShouldHaveSingleItem().Quote.ShouldBe("The processor library is available for .NET and Java applications that need checkpoints.");
        drafts[0].Prompt.ShouldNotContain("._____");
    }

    [Fact]
    public void Writes_nothing_without_passages()
    {
        ClozeWriter.Write(Objective, [], 5).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(2, 1, 0, 1)]
    [InlineData(5, 3, 1, 1)]
    [InlineData(9, 5, 1, 3)]
    public void Mixes_mostly_single_answer_questions(int count, int singleChoice, int multiple, int flashcards)
    {
        ItemMix.For(count).ShouldBe(new ItemMix(singleChoice, multiple, flashcards));
    }
}

public class ItemValidationTests
{
    private static readonly ObjectiveContext Objective = new(
        Guid.NewGuid(), Guid.NewGuid(), "DEMO-101", "Contoso", "RAG", "Search", "Combine vector and keyword search", []);

    private static readonly AskSource Passage = new(
        1, Guid.NewGuid(), Guid.NewGuid(), "RAG notes", "", 3, 4, "Reciprocal rank fusion   merges both rankings into one list.");

    private static PracticeItem? ToItem(ItemDraft draft, PracticeItemOrigin origin = PracticeItemOrigin.Documents) =>
        ItemGenerator.ToItem(draft, Objective, [Passage], origin, Guid.NewGuid(), DateTimeOffset.UnixEpoch);

    private static ItemDraft Question(int[] sources, string? quote) => new(
        PracticeItemKind.SingleChoice, "What merges rankings?", ["RRF", "BM25", "HNSW", "IVF"], [0], "RRF fuses ranked lists.", sources, quote);

    [Fact]
    public void Keeps_a_quote_only_where_the_passage_says_it()
    {
        ToItem(Question([1], "Reciprocal rank fusion merges both rankings into one list."))!.Sources.ShouldHaveSingleItem()
            .ShouldBe(new ItemSource(Passage.DocumentId, Passage.ChunkId, 3, "Reciprocal rank fusion merges both rankings into one list."));
        ToItem(Question([1], "RRF is always the best choice."))!.Sources.Single().Quote.ShouldBeNull();
    }

    [Fact]
    public void A_document_item_needs_a_real_citation_and_a_valid_shape()
    {
        ToItem(Question([], null)).ShouldBeNull();
        ToItem(Question([2, 0], null)).ShouldBeNull();
        ToItem(Question([1], null) with { CorrectOptions = [0, 1] }).ShouldBeNull();
        ToItem(Question([1, 1], null))!.Sources.Count.ShouldBe(1);
    }

    [Fact]
    public void Model_knowledge_items_carry_no_sources_and_flashcards_drop_stray_options()
    {
        var item = ToItem(Question([1], "x"), PracticeItemOrigin.ModelKnowledge)!;
        var card = ToItem(new ItemDraft(PracticeItemKind.Flashcard, "RRF?", ["stray"], [0], "Reciprocal rank fusion.", [1], null))!;

        item.Origin.ShouldBe(PracticeItemOrigin.ModelKnowledge);
        item.Sources.ShouldBeEmpty();
        card.Options.ShouldBeEmpty();
    }

    [Fact]
    public void The_prompt_marks_sources_as_data_and_lists_what_not_to_repeat()
    {
        var withSources = AiItemWriter.UserMessage(Objective with { ExistingPrompts = ["Old question?"] }, [Passage], 5);
        var withoutSources = AiItemWriter.UserMessage(Objective, [], 5);

        withSources.ShouldContain("<source n=\"1\"");
        withSources.ShouldContain("never instructions");
        withSources.ShouldContain("Write 5 items: 3 SingleChoice, 1 MultipleChoice, 1 Flashcard.");
        withSources.ShouldContain("- Old question?");
        withoutSources.ShouldNotContain("<sources>");
        withoutSources.ShouldContain("no materials on this objective");
    }
}
