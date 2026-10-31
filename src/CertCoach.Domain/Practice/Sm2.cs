namespace CertCoach.Domain.Practice;

/// <summary>How well the learner recalled an item, on the buttons a flashcard shows.</summary>
/// <remarks>Values are SM-2 quality grades. A question answered correctly counts as Good, a wrong one as Again.</remarks>
public enum ReviewGrade
{
    /// <summary>Forgot it. The item starts over tomorrow.</summary>
    Again = 1,

    /// <summary>Recalled with difficulty. Still a pass, but the item's ease drops.</summary>
    Hard = 3,

    Good = 4,

    /// <summary>Recalled at once. The ease rises.</summary>
    Easy = 5,
}

/// <summary>An item's spaced-repetition state.</summary>
public readonly record struct Sm2State(double EaseFactor, int IntervalDays, int Repetitions, int Lapses);

/// <summary>
/// The SM-2 algorithm (Wozniak, 1990). A pass grows the interval: 1 day, then 6, then the previous interval times
/// the ease factor. A fail resets the repetitions and brings the item back the next day. Every grade adjusts the
/// ease factor, which never drops below 1.3.
/// </summary>
public static class Sm2
{
    public const double MinEaseFactor = 1.3;

    public static Sm2State Next(Sm2State state, ReviewGrade grade)
    {
        var q = (int)grade;
        var ease = Math.Max(MinEaseFactor, state.EaseFactor + (0.1 - ((5 - q) * (0.08 + ((5 - q) * 0.02)))));

        if (q < 3)
        {
            // Forgetting something new isn't a lapse; forgetting something learned is.
            return new Sm2State(ease, 1, 0, state.Lapses + (state.Repetitions > 0 ? 1 : 0));
        }

        var repetitions = state.Repetitions + 1;
        var interval = repetitions switch
        {
            1 => 1,
            2 => 6,
            _ => (int)Math.Round(state.IntervalDays * state.EaseFactor, MidpointRounding.AwayFromZero),
        };
        return new Sm2State(ease, interval, repetitions, state.Lapses);
    }
}
