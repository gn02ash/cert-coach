using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CertCoach.Application.Billing;

/// <summary>The learner's AI allowance for this month: how much is left, whether a model call may go ahead, and what it used.</summary>
public sealed class AiAllowance(
    ICertCoachDbContext db, ICurrentUser user, IAiUsageLedger ledger, TimeProvider time, BillingOptions options)
{
    public async Task<AiAllowanceView> GetAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var month = AiUsage.MonthOf(now);
        var plan = await PlanAsync(now, ct);
        var proUntil = await db.BillingAccounts.Where(a => a.UserId == user.UserId).Select(a => a.ProUntil).SingleOrDefaultAsync(ct);
        var used = await db.AiUsage.Where(u => u.UserId == user.UserId && u.Month == month).Select(u => u.Units).SingleOrDefaultAsync(ct);
        var allowed = Allowed(plan);
        return new AiAllowanceView(
            options.Enabled,
            plan,
            plan == Plan.Pro ? proUntil : null,
            used,
            allowed,
            allowed == 0 ? 100 : (int)Math.Min(100, used * 100 / allowed),
            new DateTimeOffset(month.AddMonths(1), TimeOnly.MinValue, TimeSpan.Zero));
    }

    /// <summary>Always true when billing is off.</summary>
    public async Task<bool> HasRemainingAsync(CancellationToken ct)
    {
        if (!options.Enabled)
        {
            return true;
        }

        var now = time.GetUtcNow();
        var month = AiUsage.MonthOf(now);
        var used = await db.AiUsage.Where(u => u.UserId == user.UserId && u.Month == month).Select(u => u.Units).SingleOrDefaultAsync(ct);
        return used < Allowed(await PlanAsync(now, ct));
    }

    public async Task EnsureAvailableAsync(CancellationToken ct)
    {
        if (!await HasRemainingAsync(ct))
        {
            var plan = await PlanAsync(time.GetUtcNow(), ct);
            throw new AiAllowanceExceededException(plan == Plan.Free
                ? "You've used this month's free AI allowance. Upgrade to Pro for more, or keep studying with everything that doesn't need AI."
                : "You've used this month's AI allowance. It resets on the 1st.");
        }
    }

    /// <summary>
    /// Counts a finished call. Not cancellable: the model has already been paid for. A call before the limit always
    /// completes, so a learner may end the month a little over it.
    /// </summary>
    public Task RecordAsync(UsageDetails? usage, CancellationToken ct)
    {
        if (usage is null || user.UserId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return ledger.AddAsync(user.UserId, AiUsage.MonthOf(time.GetUtcNow()), Units(usage), ct);
    }

    internal long Units(UsageDetails usage) =>
        (usage.InputTokenCount ?? 0) + (options.OutputTokenWeight * (usage.OutputTokenCount ?? 0));

    private long Allowed(Plan plan) => plan == Plan.Pro ? options.ProMonthlyUnits : options.FreeMonthlyUnits;

    private async Task<Plan> PlanAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.BillingAccounts.Where(a => a.UserId == user.UserId).SingleOrDefaultAsync(ct) is { } account
            ? account.PlanAt(now)
            : Plan.Free;
}

/// <param name="Enforced">False when billing is off: nothing is limited and the plan is irrelevant.</param>
/// <param name="PercentUsed">0–100, rounded down.</param>
/// <param name="ResetsAt">The start of next month, UTC.</param>
public sealed record AiAllowanceView(
    bool Enforced, Plan Plan, DateTimeOffset? ProUntil, long UnitsUsed, long UnitsAllowed, int PercentUsed, DateTimeOffset ResetsAt);
