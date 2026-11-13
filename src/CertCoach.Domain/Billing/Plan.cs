namespace CertCoach.Domain.Billing;

/// <summary>What a learner pays for. Only the monthly AI allowance differs; every feature works on both.</summary>
public enum Plan
{
    Free,
    Pro,
}
