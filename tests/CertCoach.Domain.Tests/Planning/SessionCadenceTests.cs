using CertCoach.Domain.Planning;

namespace CertCoach.Domain.Tests.Planning;

public class SessionCadenceTests
{
    [Fact]
    public void Five_hours_over_every_day_gives_one_session_per_day()
    {
        var cadence = SessionCadence.For(5m, StudyDays.Everyday);

        cadence.SessionsPerWeek.ShouldBe(7);
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            cadence.SessionsOn(day).ShouldBe(1);
        }
    }

    [Fact]
    public void Fewer_sessions_than_study_days_are_spread_out_not_bunched()
    {
        // 3 h = four 45-minute sessions over five weekdays: one day off, no day doubled.
        var cadence = SessionCadence.For(3m, StudyDays.Weekdays);

        cadence.SessionsPerWeek.ShouldBe(4);
        cadence.SessionMinutes.ShouldBe(45);
        StudyDays.Weekdays.ToDaysOfWeek().ShouldAllBe(d => cadence.SessionsOn(d) <= 1);
        cadence.SessionsOn(DayOfWeek.Saturday).ShouldBe(0);
        cadence.SessionsOn(DayOfWeek.Sunday).ShouldBe(0);
    }

    [Fact]
    public void Second_session_is_added_only_when_hours_do_not_fit_in_one()
    {
        // 4 h on three days cannot fit in three 45-minute sessions.
        var cadence = SessionCadence.For(4m, StudyDays.Monday | StudyDays.Wednesday | StudyDays.Friday);

        cadence.SessionsPerWeek.ShouldBe(6);
        cadence.SessionMinutes.ShouldBe(40);
        cadence.SessionsOn(DayOfWeek.Monday).ShouldBe(2);
        cadence.IsCapped.ShouldBeFalse();
    }

    [Fact]
    public void Hours_beyond_two_long_sessions_per_day_are_capped()
    {
        var cadence = SessionCadence.For(10m, StudyDays.Saturday | StudyDays.Sunday);

        cadence.IsCapped.ShouldBeTrue();
        cadence.SessionsPerWeek.ShouldBe(4);
        cadence.SessionMinutes.ShouldBe(SessionCadence.MaxSessionMinutes);
    }

    [Fact]
    public void Tiny_budgets_still_get_one_minimum_length_session()
    {
        var cadence = SessionCadence.For(0.25m, StudyDays.Everyday);

        cadence.SessionsPerWeek.ShouldBe(1);
        cadence.SessionMinutes.ShouldBe(SessionCadence.MinSessionMinutes);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.5)]
    [InlineData(2.5)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(20)]
    public void Session_length_is_always_between_30_and_45_minutes(double hours)
    {
        var cadence = SessionCadence.For((decimal)hours, StudyDays.Weekdays);

        cadence.SessionMinutes.ShouldBeInRange(SessionCadence.MinSessionMinutes, SessionCadence.MaxSessionMinutes);
        StudyDays.Weekdays.ToDaysOfWeek().ShouldAllBe(d => cadence.SessionsOn(d) <= SessionCadence.MaxSessionsPerDay);
    }
}
