namespace CertCoach.Application.Abstractions;

/// <summary>Adds model use to a learner's monthly total in one atomic step, so parallel calls never lose a count.</summary>
public interface IAiUsageLedger
{
    Task AddAsync(Guid userId, DateOnly month, long units, CancellationToken ct);
}
