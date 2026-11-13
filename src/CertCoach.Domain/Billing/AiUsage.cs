namespace CertCoach.Domain.Billing;

/// <summary>
/// How much of the model a learner used in one calendar month (UTC). Units weigh output tokens more than input
/// tokens, the way model prices do, so the allowance tracks what the learner actually costs.
/// </summary>
public sealed class AiUsage
{
    private AiUsage()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; private set; }

    public long Units { get; private set; }

    public int Calls { get; private set; }

    public static DateOnly MonthOf(DateTimeOffset now) => new(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);
}
