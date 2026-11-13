namespace CertCoach.Application.Billing;

/// <summary>
/// Plans and their monthly AI allowance, under <c>Billing</c>. Off by default: a self-hosted instance pays its own
/// model bill, so nobody is limited. Turn it on when other people use an instance whose bill you pay.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    public bool Enabled { get; set; }

    /// <summary>About $0.40 a month on Claude Opus 5.5 ($4 per million input tokens).</summary>
    public long FreeMonthlyUnits { get; set; } = 100_000;

    /// <summary>At most about $6 a month on Claude Opus 5.5, for a $12 plan; most learners use far less.</summary>
    public long ProMonthlyUnits { get; set; } = 1_500_000;

    /// <summary>One output token counts as this many units, and an input token as one: output costs 5× input on Claude.</summary>
    public int OutputTokenWeight { get; set; } = 5;
}
