using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice.Generation;

/// <summary>Write items for one objective, one domain, or (with neither) the whole certification.</summary>
public sealed record GenerateItemsRequest(Guid CertificationId, Guid? DomainId = null, Guid? ObjectiveId = null, int PerObjective = 5);

/// <param name="UsesAi">
/// False in offline mode, where items are fill-in-the-blank cards and questions made from the learner's passages.
/// </param>
public sealed record GenerationJobView(
    Guid Id,
    Guid CertificationId,
    GenerationStatus Status,
    int Objectives,
    int ObjectivesDone,
    int PerObjective,
    int ItemsCreated,
    int ItemsRejected,
    int ObjectivesWithoutMaterial,
    bool UsesAi,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Wakes the generation worker when a run is started.</summary>
public interface IGenerationSignal
{
    void Notify();
}
