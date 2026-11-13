using CertCoach.Domain.Billing;

namespace CertCoach.Domain.Tests.Billing;

public class BillingAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Is_free_until_pro_is_granted_and_free_again_once_it_runs_out()
    {
        var account = BillingAccount.Open(Guid.NewGuid(), Now);
        account.PlanAt(Now).ShouldBe(Plan.Free);

        account.GrantProUntil(Now.AddDays(30), Now);

        account.PlanAt(Now.AddDays(29)).ShouldBe(Plan.Pro);
        account.PlanAt(Now.AddDays(30)).ShouldBe(Plan.Free);
    }

    [Fact]
    public void A_shorter_grant_never_takes_away_access_already_given()
    {
        var account = BillingAccount.Open(Guid.NewGuid(), Now);
        account.GrantProUntil(Now.AddDays(90), Now);

        account.GrantProUntil(Now.AddDays(30), Now.AddDays(1));

        account.ProUntil.ShouldBe(Now.AddDays(90));
        account.UpdatedAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Usage_is_counted_per_calendar_month_in_utc()
    {
        AiUsage.MonthOf(new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.FromHours(-5))).ShouldBe(new DateOnly(2026, 12, 1));
        AiUsage.MonthOf(Now).ShouldBe(new DateOnly(2026, 11, 1));
    }
}
