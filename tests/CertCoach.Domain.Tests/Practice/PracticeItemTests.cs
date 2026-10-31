using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;

namespace CertCoach.Domain.Tests.Practice;

public class PracticeItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly ItemSource Source = new(Guid.NewGuid(), Guid.NewGuid(), 3, "Hybrid search merges rankings.");

    private static PracticeItem Create(
        PracticeItemKind kind,
        string[]? options,
        int[]? correct,
        string answer = "Because.",
        PracticeItemOrigin origin = PracticeItemOrigin.Manual,
        ItemSource[]? sources = null) =>
        PracticeItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind, origin, " Which merges rankings? ", options, correct, answer, sources, Now);

    [Fact]
    public void A_new_item_is_active_unscheduled_and_trimmed()
    {
        var item = Create(PracticeItemKind.SingleChoice, [" RRF ", "BM25", "HNSW"], [0]);

        item.Status.ShouldBe(PracticeItemStatus.Active);
        item.IsNew.ShouldBeTrue();
        item.DueOn.ShouldBeNull();
        item.EaseFactor.ShouldBe(PracticeItem.InitialEaseFactor);
        item.Prompt.ShouldBe("Which merges rankings?");
        item.Options.ShouldBe(["RRF", "BM25", "HNSW"]);
        item.Fingerprint.ShouldBe("whichmergesrankings");
    }

    [Theory]
    [InlineData(PracticeItemKind.SingleChoice, new[] { "A", "B" }, new[] { 0 }, "3 to 8 options")]
    [InlineData(PracticeItemKind.SingleChoice, new[] { "A", "B", "C" }, new[] { 0, 1 }, "exactly one")]
    [InlineData(PracticeItemKind.SingleChoice, new[] { "A", "b", "B" }, new[] { 0 }, "the same")]
    [InlineData(PracticeItemKind.SingleChoice, new[] { "A", "B", "C" }, new[] { 3 }, "doesn't exist")]
    [InlineData(PracticeItemKind.MultipleChoice, new[] { "A", "B", "C", "D" }, new[] { 1 }, "at least two")]
    [InlineData(PracticeItemKind.MultipleChoice, new[] { "A", "B", "C" }, new[] { 0, 1, 2 }, "at least one wrong")]
    [InlineData(PracticeItemKind.Flashcard, new[] { "A" }, new int[0], "no options")]
    public void Rejects_questions_that_cannot_be_answered_fairly(PracticeItemKind kind, string[] options, int[] correct, string message)
    {
        Should.Throw<DomainException>(() => Create(kind, options, correct)).Message.ShouldContain(message);
    }

    [Fact]
    public void A_flashcard_needs_a_back_and_a_generated_item_needs_a_citation()
    {
        Should.Throw<DomainException>(() => Create(PracticeItemKind.Flashcard, null, null, answer: " "));
        Should.Throw<DomainException>(() => Create(PracticeItemKind.Flashcard, null, null, origin: PracticeItemOrigin.Documents));
        Create(PracticeItemKind.Flashcard, null, null, origin: PracticeItemOrigin.Documents, sources: [Source]).Sources.ShouldBe([Source]);
    }

    [Fact]
    public void Multiple_answer_questions_need_every_correct_option_selected()
    {
        var item = Create(PracticeItemKind.MultipleChoice, ["A", "B", "C", "D"], [2, 0]);

        item.CorrectOptions.ShouldBe([0, 2]);
        item.IsCorrect([2, 0]).ShouldBeTrue();
        item.IsCorrect([0]).ShouldBeFalse();
        item.IsCorrect([0, 1, 2]).ShouldBeFalse();
    }

    [Fact]
    public void Editing_keeps_the_kind_and_schedule_and_a_failed_edit_changes_nothing()
    {
        var item = Create(PracticeItemKind.SingleChoice, ["A", "B", "C"], [0]);

        item.Edit("New question?", ["A", "B", "C", "D"], [3], "New reason.");
        Should.Throw<DomainException>(() => item.Edit("Bad", ["A", "B"], [0], ""));

        item.Prompt.ShouldBe("New question?");
        item.CorrectOptions.ShouldBe([3]);
        item.Kind.ShouldBe(PracticeItemKind.SingleChoice);
        item.IsNew.ShouldBeTrue();
    }
}
