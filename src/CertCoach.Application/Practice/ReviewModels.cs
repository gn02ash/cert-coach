using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice;

/// <summary>An item to answer. Questions come without their answers; a flashcard carries its back, shown on request.</summary>
/// <param name="ChooseCount">How many options to select: 1, or the number of correct options for "choose several".</param>
/// <param name="Intervals">For flashcards: the days until the next review after each grade.</param>
public sealed record ReviewCard(
    Guid Id,
    Guid CertificationId,
    string ExamCode,
    Guid? ObjectiveId,
    string? ObjectiveText,
    string? Domain,
    PracticeItemKind Kind,
    PracticeItemOrigin Origin,
    string Prompt,
    IReadOnlyList<string> Options,
    int ChooseCount,
    string? Back,
    bool IsNew,
    ReviewIntervals? Intervals);

public sealed record ReviewIntervals(int Again, int Hard, int Good, int Easy);

/// <param name="Due">Items whose review date has come, across the scope, not just those in <paramref name="Cards"/>.</param>
/// <param name="NewAvailable">Never-reviewed items waiting.</param>
/// <param name="NewLeftToday">How many new items today's limit still allows.</param>
public sealed record ReviewSet(
    DateOnly Today, int Due, int NewAvailable, int NewLeftToday, int ReviewedToday, IReadOnlyList<ReviewCard> Cards);

/// <summary>Options for a question, or a grade for a flashcard.</summary>
public sealed record AnswerRequest(IReadOnlyList<int>? SelectedOptions, ReviewGrade? Grade, ReviewSource Source = ReviewSource.Review);

/// <param name="Correct">Null for flashcards.</param>
public sealed record AnswerResult(
    bool? Correct, IReadOnlyList<int> CorrectOptions, string Answer, IReadOnlyList<ItemSourceView> Sources, int IntervalDays, DateOnly DueOn);
