namespace CertCoach.Domain.Billing;

/// <summary>
/// A learner's paid access. Pro is a date rather than a flag, so a subscription, a one-off pass and a manual grant
/// all work the same way: the learner is Pro until <see cref="ProUntil"/>, and nothing has to run when it passes.
/// A learner without an account is on the free plan.
/// </summary>
public sealed class BillingAccount
{
    private BillingAccount()
    {
    }

    public Guid UserId { get; private set; }

    public DateTimeOffset? ProUntil { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static BillingAccount Open(Guid userId, DateTimeOffset now) => new() { UserId = userId, UpdatedAt = now };

    public Plan PlanAt(DateTimeOffset now) => ProUntil > now ? Plan.Pro : Plan.Free;

    /// <summary>Gives Pro until at least <paramref name="until"/>; never shortens access already granted.</summary>
    public void GrantProUntil(DateTimeOffset until, DateTimeOffset now)
    {
        if (ProUntil is null || until > ProUntil)
        {
            ProUntil = until;
        }

        UpdatedAt = now;
    }
}
