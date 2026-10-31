using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;

namespace CertCoach.Domain.Tests.Practice;

public class MockExamTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid[] Items = [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())];

    private static MockExam Exam() => MockExam.Start(Guid.NewGuid(), Guid.NewGuid(), null, Items, 30, Now);

    [Fact]
    public void Answers_can_change_until_the_deadline_and_a_short_grace()
    {
        var exam = Exam();

        exam.SaveAnswer(Items[0], [2], Now);
        exam.SaveAnswer(Items[0], [3, 1], Now.AddMinutes(30).AddSeconds(10));

        exam.SelectedFor(Items[0]).ShouldBe([1, 3]);
        exam.SelectedFor(Items[1]).ShouldBeEmpty();
        exam.IsOver(Now.AddMinutes(31)).ShouldBeTrue();
        Should.Throw<DomainException>(() => exam.SaveAnswer(Items[1], [0], Now.AddMinutes(31)));
        Should.Throw<DomainException>(() => exam.SaveAnswer(Guid.NewGuid(), [0], Now));
    }

    [Fact]
    public void A_submitted_exam_is_closed_and_a_late_submit_is_dated_at_the_deadline()
    {
        var exam = Exam();

        exam.Submit(3, Now.AddHours(2));

        exam.SubmittedAt.ShouldBe(exam.Deadline);
        exam.CorrectCount.ShouldBe(3);
        Should.Throw<DomainException>(() => exam.Submit(3, Now));
        Should.Throw<DomainException>(() => exam.SaveAnswer(Items[0], [0], Now));
    }

    [Fact]
    public void Needs_enough_questions_and_a_sensible_time_limit()
    {
        Should.Throw<DomainException>(() => MockExam.Start(Guid.NewGuid(), Guid.NewGuid(), null, Items[..4], 30, Now)).Message.ShouldContain("Generate more");
        Should.Throw<DomainException>(() => MockExam.Start(Guid.NewGuid(), Guid.NewGuid(), null, Items, 2, Now));
    }

    [Fact]
    public void A_question_left_blank_in_an_exam_is_wrong_but_needs_an_answer_elsewhere()
    {
        var item = PracticeItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, PracticeItemKind.SingleChoice, PracticeItemOrigin.Manual, "Q?", ["A", "B", "C"], [0], "", null, Now);

        Should.Throw<DomainException>(() => item.Submit([], ReviewSource.Review, DateOnly.FromDateTime(Now.Date), Now));
        item.Submit([], ReviewSource.MockExam, DateOnly.FromDateTime(Now.Date), Now).Correct.ShouldBe(false);
    }
}
