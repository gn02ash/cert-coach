using CertCoach.Domain.Common;
using CertCoach.Domain.Planning;
using static CertCoach.Domain.Tests.Planning.Outlines;

namespace CertCoach.Domain.Tests.Planning;

public class StudyPlanTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public void Creating_a_plan_generates_sessions_in_sequence()
    {
        var (plan, warnings) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);

        warnings.ShouldBeEmpty();
        plan.Sessions.Count.ShouldBe(28);
        plan.Sessions.Select(s => s.Sequence).ShouldBe(Enumerable.Range(0, 28));
        plan.Sessions.ShouldAllBe(s => s.Status == SessionStatus.Planned);
    }

    [Fact]
    public void A_plan_cannot_use_another_users_certification()
    {
        Should.Throw<DomainException>(() => StudyPlan.Create(Owner, Certification(Guid.NewGuid()), Settings(weeks: 4), Now));
    }

    [Fact]
    public void Missed_learn_content_is_rescheduled_and_the_review_buffer_absorbs_it()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);
        var reviewsBefore = Count(plan, SessionKind.Review);
        var first = plan.Sessions[0];

        var warnings = plan.MarkSessionMissed(first.Id, Monday);

        warnings.ShouldBeEmpty();
        plan.Shortfall.ShouldBeNull();
        plan.Sessions.Single(s => s.Id == first.Id).Status.ShouldBe(SessionStatus.Missed);

        var planned = plan.Sessions.Where(s => s.Status == SessionStatus.Planned).ToList();
        planned.ShouldContain(s => s.Kind == SessionKind.Learn && s.ObjectiveIds.SequenceEqual(first.ObjectiveIds));
        planned.ShouldAllBe(s => s.ScheduledDate > Monday); // today's only slot was used by the missed session
        Count(plan, SessionKind.Review).ShouldBe(reviewsBefore - 1);
        Count(plan, SessionKind.MockExam).ShouldBe(2);
        planned.Max(s => s.ScheduledDate).ShouldBe(plan.ExamDate.AddDays(-1));
    }

    [Fact]
    public void Rescheduling_never_touches_completed_history()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);
        var done = plan.Sessions.Take(3).ToList();
        done.ForEach(s => plan.CompleteSession(s.Id, Now));
        var snapshot = done.Select(s => (s.Id, s.ScheduledDate, s.Status)).ToList();

        plan.MarkSessionMissed(plan.Sessions[3].Id, Monday.AddDays(3));

        plan.Sessions.Where(s => s.Status == SessionStatus.Completed)
            .Select(s => (s.Id, s.ScheduledDate, s.Status))
            .ShouldBe(snapshot);
    }

    [Fact]
    public void Overdue_planned_sessions_are_treated_as_missed()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);
        var overdueObjectives = plan.Sessions.Take(2).SelectMany(s => s.ObjectiveIds).ToList();

        var warnings = plan.RescheduleOverdue(Monday.AddDays(2));

        warnings.ShouldNotBeNull();
        plan.Sessions.Count(s => s.Status == SessionStatus.Missed).ShouldBe(2);
        var plannedObjectives = plan.Sessions.Where(s => s.Status == SessionStatus.Planned).SelectMany(s => s.ObjectiveIds).ToList();
        overdueObjectives.ShouldBeSubsetOf(plannedObjectives);
        plan.Sessions.Where(s => s.Status == SessionStatus.Planned).ShouldAllBe(s => s.ScheduledDate >= Monday.AddDays(2));
    }

    [Fact]
    public void Nothing_overdue_means_no_reschedule()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);

        plan.RescheduleOverdue(Monday).ShouldBeNull();
    }

    [Fact]
    public void A_missed_review_is_dropped_rather_than_rescheduled()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);
        var review = plan.Sessions.First(s => s.Kind == SessionKind.Review);
        var learnBefore = plan.Sessions.Count(s => s.Kind == SessionKind.Learn);

        // Everything before the review is done, then the review is missed.
        plan.Sessions.Where(s => s.Sequence < review.Sequence).ToList().ForEach(s => plan.CompleteSession(s.Id, Now));
        plan.MarkSessionMissed(review.Id, review.ScheduledDate);

        plan.Sessions.Count(s => s.Kind == SessionKind.Learn).ShouldBe(learnBefore);
        plan.Sessions.Count(s => s.Kind == SessionKind.Review && s.Status == SessionStatus.Planned)
            .ShouldBe(plan.Sessions.Count(s => s.Kind == SessionKind.Review) - 1);
    }

    [Fact]
    public void Missing_too_much_near_the_exam_reports_a_shortfall_but_keeps_all_content()
    {
        // Two weeks, 9 objectives, 14 slots. Miss the whole first week.
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 2), Now);
        var allObjectives = plan.Sessions.SelectMany(s => s.ObjectiveIds).ToHashSet();

        var warnings = plan.RescheduleOverdue(Monday.AddDays(9));

        warnings.ShouldNotBeNull();
        warnings.ShouldContain(w => w.Code == PlanWarningCode.ObjectivesPacked);
        plan.Shortfall.ShouldNotBeNull();
        plan.Shortfall.SuggestedExamDate.ShouldBeGreaterThan(plan.ExamDate);
        var planned = plan.Sessions.Where(s => s.Status == SessionStatus.Planned).ToList();
        planned.Count.ShouldBe(5);
        planned.ShouldAllBe(s => s.Kind == SessionKind.Learn);
        planned.SelectMany(s => s.ObjectiveIds).ToHashSet().ShouldBe(allObjectives, ignoreOrder: true);
    }

    [Fact]
    public void Future_sessions_cannot_be_marked_missed()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);

        Should.Throw<DomainException>(() => plan.MarkSessionMissed(plan.Sessions[5].Id, Monday));
    }

    [Fact]
    public void A_session_cannot_be_completed_twice()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);
        var session = plan.Sessions[0];
        plan.CompleteSession(session.Id, Now);

        Should.Throw<DomainException>(() => plan.CompleteSession(session.Id, Now));
    }

    [Fact]
    public void Sequence_stays_contiguous_and_date_ordered_after_rescheduling()
    {
        var (plan, _) = StudyPlan.Create(Owner, Certification(Owner), Settings(weeks: 4), Now);
        plan.CompleteSession(plan.Sessions[0].Id, Now);
        plan.MarkSessionMissed(plan.Sessions[1].Id, Monday.AddDays(1));

        var sessions = plan.Sessions;
        sessions.Select(s => s.Sequence).ShouldBe(Enumerable.Range(0, sessions.Count));
        sessions.Select(s => s.ScheduledDate).ShouldBeInOrder();
    }

    private static int Count(StudyPlan plan, SessionKind kind) =>
        plan.Sessions.Count(s => s.Kind == kind && s.Status == SessionStatus.Planned);
}
