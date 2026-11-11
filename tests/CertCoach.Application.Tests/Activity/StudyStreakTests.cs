using CertCoach.Application.Activity;

namespace CertCoach.Application.Tests.Activity;

public class StudyStreakTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static HashSet<DateOnly> DaysAgo(params int[] offsets) => offsets.Select(o => Today.AddDays(-o)).ToHashSet();

    [Fact]
    public void Counts_consecutive_days_up_to_today()
    {
        StudyStreak.Compute(DaysAgo(0, 1, 2, 4), Today).ShouldBe(new Streak(3, 3, true));
    }

    [Fact]
    public void Yesterdays_streak_is_alive_until_today_ends()
    {
        StudyStreak.Compute(DaysAgo(1, 2), Today).ShouldBe(new Streak(2, 2, false));
    }

    [Fact]
    public void A_missed_day_ends_the_streak_but_keeps_the_record()
    {
        StudyStreak.Compute(DaysAgo(2, 3, 4, 5, 9), Today).ShouldBe(new Streak(0, 4, false));
    }

    [Fact]
    public void No_study_is_no_streak()
    {
        StudyStreak.Compute(DaysAgo(), Today).ShouldBe(new Streak(0, 0, false));
    }
}
