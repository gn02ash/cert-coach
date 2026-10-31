using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice;

/// <param name="ObjectiveText">Null when the outline changed and no objective with the item's text remains.</param>
/// <param name="DueOn">Null until the first review.</param>
public sealed record PracticeItemView(
    Guid Id,
    Guid CertificationId,
    Guid? ObjectiveId,
    string? ObjectiveText,
    PracticeItemKind Kind,
    PracticeItemOrigin Origin,
    PracticeItemStatus Status,
    string Prompt,
    IReadOnlyList<string> Options,
    IReadOnlyList<int> CorrectOptions,
    string Answer,
    IReadOnlyList<ItemSourceView> Sources,
    DateOnly? DueOn,
    int Repetitions,
    DateTimeOffset CreatedAt);

/// <param name="DocumentTitle">Null when the document has since been deleted.</param>
public sealed record ItemSourceView(Guid DocumentId, string? DocumentTitle, int Page, string? Quote);

/// <summary>A practice item the learner writes. Options and correct options are left out for flashcards.</summary>
public sealed record CreatePracticeItemRequest(
    Guid CertificationId,
    Guid? ObjectiveId,
    PracticeItemKind Kind,
    string Prompt,
    IReadOnlyList<string>? Options,
    IReadOnlyList<int>? CorrectOptions,
    string Answer);

public sealed record UpdatePracticeItemRequest(
    string Prompt, IReadOnlyList<string>? Options, IReadOnlyList<int>? CorrectOptions, string Answer);

public sealed record SetPracticeItemStatusRequest(PracticeItemStatus Status);
