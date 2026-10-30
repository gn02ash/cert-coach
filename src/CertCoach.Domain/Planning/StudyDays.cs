namespace CertCoach.Domain.Planning;

[Flags]
public enum StudyDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Everyday = Weekdays | Saturday | Sunday,
}

public static class StudyDaysExtensions
{
    /// <summary>The selected days in Monday-first order.</summary>
    public static IReadOnlyList<DayOfWeek> ToDaysOfWeek(this StudyDays days) =>
        MondayFirst.Where(d => days.Includes(d)).ToList();

    public static bool Includes(this StudyDays days, DayOfWeek day) => (days & ToFlag(day)) != 0;

    private static readonly DayOfWeek[] MondayFirst =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private static StudyDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => StudyDays.Monday,
        DayOfWeek.Tuesday => StudyDays.Tuesday,
        DayOfWeek.Wednesday => StudyDays.Wednesday,
        DayOfWeek.Thursday => StudyDays.Thursday,
        DayOfWeek.Friday => StudyDays.Friday,
        DayOfWeek.Saturday => StudyDays.Saturday,
        DayOfWeek.Sunday => StudyDays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };
}
