using CertCoach.Domain.Common;

namespace CertCoach.Domain.Practice;

public enum GenerationStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// A request to write practice items for a list of objectives, worked through in the background one objective at
/// a time. Progress is saved after each objective, so a restart resumes where it stopped and a cancel takes effect
/// before the next one.
/// </summary>
public sealed class GenerationJob
{
    public const int MinPerObjective = 1;
    public const int MaxPerObjective = 10;

    private List<Guid> _objectiveIds = [];

    private GenerationJob()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CertificationId { get; private set; }

    /// <summary>In the order they are worked through.</summary>
    public IReadOnlyList<Guid> ObjectiveIds => _objectiveIds;

    public int PerObjective { get; private set; }
    public GenerationStatus Status { get; private set; }

    /// <summary>How many of <see cref="ObjectiveIds"/> are done, which is also the index of the next one.</summary>
    public int ObjectivesDone { get; private set; }

    public int ItemsCreated { get; private set; }

    /// <summary>Generated items dropped by validation or as duplicates.</summary>
    public int ItemsRejected { get; private set; }

    /// <summary>Objectives none of the learner's documents cover, so no item could cite them.</summary>
    public int ObjectivesWithoutMaterial { get; private set; }

    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Set when claimed and after each objective. A running job silent for long belongs to a stopped worker.</summary>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsActive => Status is GenerationStatus.Queued or GenerationStatus.Running;

    public static GenerationJob Create(
        Guid ownerId, Guid certificationId, IReadOnlyList<Guid> objectiveIds, int perObjective, DateTimeOffset now)
    {
        if (objectiveIds.Count == 0)
        {
            throw new DomainException("Choose at least one objective to write questions for.");
        }

        if (perObjective is < MinPerObjective or > MaxPerObjective)
        {
            throw new DomainException($"Ask for {MinPerObjective} to {MaxPerObjective} items per objective.");
        }

        return new GenerationJob
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CertificationId = certificationId,
            _objectiveIds = objectiveIds.Distinct().ToList(),
            PerObjective = perObjective,
            Status = GenerationStatus.Queued,
            CreatedAt = now,
        };
    }

    /// <summary>The next objective to work on, or null when every objective is done.</summary>
    public Guid? NextObjective => ObjectivesDone < _objectiveIds.Count ? _objectiveIds[ObjectivesDone] : null;

    public void RecordObjective(int created, int rejected, bool hadMaterial, DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException($"The generation run is already {Status.ToString().ToLowerInvariant()}.");
        }

        ObjectivesDone++;
        ItemsCreated += created;
        ItemsRejected += rejected;
        ObjectivesWithoutMaterial += hadMaterial ? 0 : 1;
        HeartbeatAt = now;
    }

    public void Complete(DateTimeOffset now) => Finish(GenerationStatus.Completed, null, now);

    public void Fail(string error, DateTimeOffset now) => Finish(GenerationStatus.Failed, error, now);

    public void Cancel(DateTimeOffset now)
    {
        if (IsActive)
        {
            Finish(GenerationStatus.Cancelled, null, now);
        }
    }

    private void Finish(GenerationStatus status, string? error, DateTimeOffset now)
    {
        Status = status;
        Error = error is { Length: > 500 } ? error[..500] : error;
        FinishedAt = now;
    }
}
