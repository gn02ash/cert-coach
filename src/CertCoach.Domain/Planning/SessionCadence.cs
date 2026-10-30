namespace CertCoach.Domain.Planning;

/// <summary>
/// Turns "hours per week on these days" into a session length and a weekly pattern.
/// Prefers one 30–45 minute session per study day and adds a second only when the
/// weekly hours cannot fit otherwise.
/// </summary>
public sealed class SessionCadence
{
    public const int MinSessionMinutes = 30;
    public const int MaxSessionMinutes = 45;
    public const int MaxSessionsPerDay = 2;

    // Indexed by (int)DayOfWeek, so Sunday = 0.
    private readonly int[] _sessionsPerDay;

    private SessionCadence(int sessionMinutes, int[] sessionsPerDay, bool isCapped)
    {
        SessionMinutes = sessionMinutes;
        _sessionsPerDay = sessionsPerDay;
        IsCapped = isCapped;
    }

    public int SessionMinutes { get; }

    public int SessionsPerWeek => _sessionsPerDay.Sum();

    /// <summary>True when the requested hours exceed two max-length sessions on every study day.</summary>
    public bool IsCapped { get; }

    public int SessionsOn(DayOfWeek day) => _sessionsPerDay[(int)day];

    public static SessionCadence For(decimal hoursPerWeek, StudyDays studyDays)
    {
        var days = studyDays.ToDaysOfWeek();
        if (days.Count == 0)
        {
            throw new ArgumentException("At least one study day is required.", nameof(studyDays));
        }

        var minutes = (double)hoursPerWeek * 60;
        var maxSessions = days.Count * MaxSessionsPerDay;

        var sessions = (int)Math.Ceiling(minutes / MaxSessionMinutes);
        if (minutes / sessions < MinSessionMinutes)
        {
            // Too few minutes for that many sessions: use fewer, longer ones.
            sessions = Math.Max(1, (int)Math.Floor(minutes / MinSessionMinutes));
        }

        var isCapped = sessions > maxSessions;
        sessions = Math.Min(sessions, maxSessions);

        var length = (int)(Math.Round(minutes / sessions / 5, MidpointRounding.AwayFromZero) * 5);
        length = Math.Clamp(length, MinSessionMinutes, MaxSessionMinutes);

        return new SessionCadence(length, Spread(sessions, days), isCapped);
    }

    /// <summary>Spreads sessions evenly over the study days (Bresenham-style), so 3 sessions on 5 days skip days evenly.</summary>
    private static int[] Spread(int sessions, IReadOnlyList<DayOfWeek> days)
    {
        var perDay = new int[7];
        var baseCount = sessions / days.Count;
        var extra = sessions % days.Count;

        for (var i = 0; i < days.Count; i++)
        {
            var getsExtra = (i + 1) * extra / days.Count > i * extra / days.Count;
            perDay[(int)days[i]] = baseCount + (getsExtra ? 1 : 0);
        }

        return perDay;
    }
}
