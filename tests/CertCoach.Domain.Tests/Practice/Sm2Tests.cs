using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;

namespace CertCoach.Domain.Tests.Practice;

public class Sm2Tests
{
    private static readonly Sm2State New = new(2.5, 0, 0, 0);

    [Fact]
    public void Passes_grow_the_interval_1_then_6_then_by_the_ease_factor()
    {
        var first = Sm2.Next(New, ReviewGrade.Good);
        var second = Sm2.Next(first, ReviewGrade.Good);
        var third = Sm2.Next(second, ReviewGrade.Good);

        (first.IntervalDays, second.IntervalDays, third.IntervalDays).ShouldBe((1, 6, 15));
        third.EaseFactor.ShouldBe(2.5); // Good leaves the ease unchanged.
    }

    [Fact]
    public void Easy_raises_the_ease_and_hard_lowers_it()
    {
        Sm2.Next(New, ReviewGrade.Easy).EaseFactor.ShouldBe(2.6, 1e-9);
        Sm2.Next(New, ReviewGrade.Hard).EaseFactor.ShouldBe(2.36, 1e-9);
    }

    [Fact]
    public void A_fail_restarts_the_item_tomorrow_and_counts_a_lapse_only_once_learned()
    {
        var learned = Sm2.Next(Sm2.Next(New, ReviewGrade.Good), ReviewGrade.Good);
        var forgotten = Sm2.Next(learned, ReviewGrade.Again);

        forgotten.ShouldBe(new Sm2State(1.96, 1, 0, 1), new Sm2StateComparer());
        Sm2.Next(New, ReviewGrade.Again).Lapses.ShouldBe(0);
        Sm2.Next(forgotten, ReviewGrade.Good).IntervalDays.ShouldBe(1);
    }

    [Fact]
    public void The_ease_never_drops_below_1_3()
    {
        var state = New;
        for (var i = 0; i < 10; i++)
        {
            state = Sm2.Next(state, ReviewGrade.Again);
        }

        state.EaseFactor.ShouldBe(Sm2.MinEaseFactor);
    }

    private sealed class Sm2StateComparer : IEqualityComparer<Sm2State>
    {
        public bool Equals(Sm2State a, Sm2State b) =>
            Math.Abs(a.EaseFactor - b.EaseFactor) < 1e-9 && (a.IntervalDays, a.Repetitions, a.Lapses) == (b.IntervalDays, b.Repetitions, b.Lapses);

        public int GetHashCode(Sm2State s) => s.IntervalDays;
    }
}

public class ReviewTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static PracticeItem Question() => PracticeItem.Create(
        Guid.NewGuid(), Guid.NewGuid(), null, PracticeItemKind.MultipleChoice, PracticeItemOrigin.Manual,
        "Which two? (Choose two.)", ["A", "B", "C", "D"], [1, 3], "B and D.", null, Now);

    private static PracticeItem Card() => PracticeItem.Create(
        Guid.NewGuid(), Guid.NewGuid(), null, PracticeItemKind.Flashcard, PracticeItemOrigin.Manual, "Front", null, null, "Back", null, Now);

    [Fact]
    public void A_correct_answer_counts_as_good_and_a_wrong_one_as_again()
    {
        var item = Question();

        var right = item.Submit([3, 1], ReviewSource.Review, Today, Now);
        right.Correct.ShouldBe(true);
        right.Grade.ShouldBe(ReviewGrade.Good);
        right.WasNew.ShouldBeTrue();
        item.DueOn.ShouldBe(Today.AddDays(1));

        var wrong = item.Submit([1], ReviewSource.Quiz, Today.AddDays(1), Now.AddDays(1));
        wrong.Correct.ShouldBe(false);
        wrong.Grade.ShouldBe(ReviewGrade.Again);
        wrong.WasNew.ShouldBeFalse();
        wrong.SelectedOptions.ShouldBe([1]);
        item.DueOn.ShouldBe(Today.AddDays(2));
    }

    [Fact]
    public void Flashcards_are_graded_and_questions_are_answered()
    {
        var card = Card();

        card.IntervalAfter(ReviewGrade.Good).ShouldBe(1);
        var log = card.Grade(ReviewGrade.Easy, ReviewSource.Review, Today, Now);
        log.Correct.ShouldBeNull();
        card.IsNew.ShouldBeFalse();
        card.IntervalAfter(ReviewGrade.Good).ShouldBe(6);

        Should.Throw<DomainException>(() => card.Submit([0], ReviewSource.Review, Today, Now));
        Should.Throw<DomainException>(() => Question().Grade(ReviewGrade.Good, ReviewSource.Review, Today, Now));
        Should.Throw<DomainException>(() => Question().Submit([], ReviewSource.Review, Today, Now));
        Should.Throw<DomainException>(() => Question().Submit([4], ReviewSource.Review, Today, Now));
    }
}
