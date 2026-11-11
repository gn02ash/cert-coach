using CertCoach.Application.Practice;
using CertCoach.Domain.Practice;

namespace CertCoach.Application.Tests.Practice;

public class AnkiExportTests
{
    private static PracticeItem Item(PracticeItemKind kind, string prompt, string[]? options, int[]? correct, string answer) =>
        PracticeItem.Create(Guid.NewGuid(), Guid.NewGuid(), null, kind, PracticeItemOrigin.Manual, prompt, options, correct, answer, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Writes_cards_and_questions_as_quoted_tab_separated_fields_with_tags()
    {
        var text = AnkiExport.Build("DEMO-101",
        [
            new(Item(PracticeItemKind.Flashcard, "What is \"RRF\"?", null, null, "Reciprocal rank fusion.\nk = 60"), "Implement RAG"),
            new(Item(PracticeItemKind.MultipleChoice, "Which two? (Choose two.)", ["RRF", "HNSW", "Borda", "BM25"], [2, 0], "Both fuse lists."), null),
        ]);

        text.ShouldBe(
            "#separator:tab\n#html:false\n#columns:Front\tBack\tTags\n#tags column:3\n" +
            "\"What is \"\"RRF\"\"?\"\t\"Reciprocal rank fusion.\nk = 60\"\t\"DEMO-101 Implement-RAG\"\n" +
            "\"Which two? (Choose two.)\n\nA. RRF\nB. HNSW\nC. Borda\nD. BM25\"\t\"A. RRF\nC. Borda\n\nBoth fuse lists.\"\t\"DEMO-101\"\n");
    }

    [Fact]
    public void A_question_without_an_explanation_shows_only_its_answer()
    {
        var text = AnkiExport.Build("X-1", [new(Item(PracticeItemKind.SingleChoice, "Q?", ["Yes", "No", "Maybe"], [1], ""), null)]);

        text.ShouldEndWith("\t\"B. No\"\t\"X-1\"\n");
    }
}
