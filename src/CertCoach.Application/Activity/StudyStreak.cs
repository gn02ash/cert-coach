namespace CertCoach.Application.Activity;

/// <param name="Current">Consecutive study days up to today, or up to yesterday while today is still open.</param>
/// <param name="Longest">The longest run of consecutive study days ever.</param>
/// <param name="StudiedToday">False while the current streak still needs today's study to carry on.</param>
public sealed record Streak(int Current, int Longest, bool StudiedToday);

/// <summary>Streaks over the days the learner studied: reviewed at least one item or completed a session.</summary>
public static class StudyStreak
{
    public static Streak Compute(IReadOnlySet<DateOnly> studyDays, DateOnly today)
    {
        var studiedToday = studyDays.Contains(today);

        // Yesterday's streak is still alive until today ends.
        var current = 0;
        for (var day = studiedToday ? today : today.AddDays(-1); studyDays.Contains(day); day = day.AddDays(-1))
        {
            current++;
        }

        var (longest, run) = (0, 0);
        DateOnly? previous = null;
        foreach (var day in studyDays.Where(d => d <= today).Order())
        {
            run = previous is { } p && day == p.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }

        return new Streak(current, longest, studiedToday);
    }
}
