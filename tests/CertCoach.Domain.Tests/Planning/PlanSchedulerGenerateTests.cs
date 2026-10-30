using CertCoach.Domain.Common;
using CertCoach.Domain.Planning;
using static CertCoach.Domain.Tests.Planning.Outlines;

namespace CertCoach.Domain.Tests.Planning;

public class PlanSchedulerGenerateTests
{
    [Fact]
    public void Every_objective_gets_at_least_one_learn_session()
    {
        var outline = ThreeDomains();

        var result = PlanScheduler.Generate(Settings(weeks: 4), outline);

        var covered = result.Sessions.Where(s => s.Kind == SessionKind.Learn).SelectMany(s => s.ObjectiveIds).ToHashSet();
        covered.ShouldBe(outline.SelectMany(d => d.ObjectiveIds).ToHashSet(), ignoreOrder: true);
        result.Warnings.ShouldBeEmpty();
        result.Shortfall.ShouldBeNull();
    }

    [Fact]
    public void Learn_sessions_are_split_in_proportion_to_domain_weight()
    {
        // Same objective count, 60/40 weights, plenty of time.
        var heavy = Weighted(60, 2);
        var light = Weighted(40, 2);

        var result = PlanScheduler.Generate(Settings(weeks: 8), [heavy, light]);

        var learn = result.Sessions.Where(s => s.Kind == SessionKind.Learn).ToList();
        var heavyShare = learn.Count(s => heavy.ObjectiveIds.Contains(s.ObjectiveIds[0])) / (double)learn.Count;
        heavyShare.ShouldBe(0.6, tolerance: 0.03);
    }

    [Fact]
    public void Domains_are_interleaved_instead_of_studied_in_blocks()
    {
        var outline = ThreeDomains();

        var result = PlanScheduler.Generate(Settings(weeks: 6), outline);

        var domainOrder = result.Sessions
            .Where(s => s.Kind == SessionKind.Learn)
            .Select(s => outline.Single(d => d.ObjectiveIds.Contains(s.ObjectiveIds[0])).DomainId)
            .ToList();
        LongestRun(domainOrder).ShouldBeLessThanOrEqualTo(2);
        domainOrder.Take(5).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Review_buffer_and_mock_exams_come_last_before_the_exam()
    {
        var settings = Settings(weeks: 6);

        var sessions = PlanScheduler.Generate(settings, ThreeDomains()).Sessions;

        var kinds = sessions.Select(s => s.Kind).ToList();
        kinds.TakeLast(2).ShouldAllBe(k => k == SessionKind.MockExam);
        kinds.Count(k => k == SessionKind.Review).ShouldBe(4); // 10% of 42 slots
        kinds.SkipWhile(k => k == SessionKind.Learn).ShouldNotContain(SessionKind.Learn);
        sessions.Where(s => s.Kind == SessionKind.Review).ShouldAllBe(s => s.FocusDomainId != null);
    }

    [Fact]
    public void Sessions_fall_on_study_days_before_the_exam_within_daily_limits()
    {
        var settings = Settings(weeks: 5, hoursPerWeek: 4m, days: StudyDays.Monday | StudyDays.Wednesday | StudyDays.Saturday);
        var cadence = SessionCadence.For(settings.HoursPerWeek, settings.StudyDays);

        var sessions = PlanScheduler.Generate(settings, ThreeDomains()).Sessions;

        sessions.ShouldAllBe(s => s.Date >= settings.StartDate && s.Date < settings.ExamDate);
        sessions.ShouldAllBe(s => settings.StudyDays.Includes(s.Date.DayOfWeek));
        sessions.ShouldAllBe(s => s.DurationMinutes == cadence.SessionMinutes);
        sessions.GroupBy(s => s.Date).ShouldAllBe(g => g.Count() <= cadence.SessionsOn(g.Key.DayOfWeek));
        sessions.Select(s => s.Date).ShouldBeInOrder();
    }

    [Fact]
    public void Short_on_time_packs_objectives_and_reports_the_shortfall()
    {
        // 9 objectives, one week of weekdays at 3 h => 4 sessions.
        var outline = ThreeDomains();
        var settings = Settings(weeks: 1, hoursPerWeek: 3m, days: StudyDays.Weekdays);

        var result = PlanScheduler.Generate(settings, outline);

        result.Sessions.Count.ShouldBe(4);
        result.Sessions.ShouldAllBe(s => s.Kind == SessionKind.Learn);
        result.Sessions.SelectMany(s => s.ObjectiveIds).ShouldBe(outline.SelectMany(d => d.ObjectiveIds), ignoreOrder: true);
        result.Warnings.ShouldContain(w => w.Code == PlanWarningCode.ObjectivesPacked);

        result.Shortfall.ShouldNotBeNull();
        result.Shortfall.MissingSessions.ShouldBe(5);
        result.Shortfall.SuggestedHoursPerWeek.ShouldBeGreaterThan(settings.HoursPerWeek);
        result.Shortfall.SuggestedExamDate.ShouldBeGreaterThan(settings.ExamDate);
    }

    [Fact]
    public void Fewer_sessions_than_domains_still_covers_everything_in_outline_order()
    {
        var outline = ThreeDomains();
        var settings = new PlanSettings(Monday, Monday.AddDays(2), 5m, StudyDays.Everyday);

        var result = PlanScheduler.Generate(settings, outline);

        result.Sessions.Count.ShouldBe(2);
        result.Sessions.SelectMany(s => s.ObjectiveIds).ShouldBe(outline.SelectMany(d => d.ObjectiveIds));
    }

    [Fact]
    public void No_study_days_before_the_exam_returns_an_empty_plan_with_a_warning()
    {
        // Saturday start, exam on Monday, weekdays only.
        var saturday = Monday.AddDays(5);
        var settings = new PlanSettings(saturday, saturday.AddDays(2), 5m, StudyDays.Weekdays);

        var result = PlanScheduler.Generate(settings, ThreeDomains());

        result.Sessions.ShouldBeEmpty();
        result.Warnings.ShouldContain(w => w.Code == PlanWarningCode.NoStudySlots);
        result.Shortfall!.MissingSessions.ShouldBe(9);
    }

    [Fact]
    public void Missing_weights_fall_back_to_equal_weighting()
    {
        var a = Weighted(0, 1);
        var b = Weighted(0, 1);

        var learn = PlanScheduler.Generate(Settings(weeks: 4), [a, b]).Sessions.Where(s => s.Kind == SessionKind.Learn).ToList();

        Math.Abs(learn.Count(s => s.ObjectiveIds[0] == a.ObjectiveIds[0]) - (learn.Count / 2)).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Same_inputs_always_give_the_same_plan()
    {
        var outline = ThreeDomains();
        var settings = Settings(weeks: 3);

        var first = PlanScheduler.Generate(settings, outline).Sessions;
        var second = PlanScheduler.Generate(settings, outline).Sessions;

        Describe(first).ShouldBe(Describe(second));
    }

    [Fact]
    public void An_outline_without_objectives_is_rejected()
    {
        Should.Throw<DomainException>(() => PlanScheduler.Generate(Settings(weeks: 2), [Weighted(100, 0)]));
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    public void Mock_exam_count_scales_with_plan_length(int slots, int expected) =>
        PlanScheduler.DesiredMockExams(slots).ShouldBe(expected);

    private static int LongestRun<T>(IReadOnlyList<T> items)
    {
        var longest = 0;
        for (int i = 0, run = 0; i < items.Count; i++)
        {
            run = i > 0 && Equals(items[i], items[i - 1]) ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static List<string> Describe(IEnumerable<SessionDraft> sessions) =>
        sessions.Select(s => $"{s.Date}|{s.Kind}|{s.FocusDomainId}|{string.Join(',', s.ObjectiveIds)}").ToList();
}
