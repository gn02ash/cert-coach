namespace CertCoach.Domain.Practice;

public enum ReviewSource
{
    /// <summary>The daily review queue.</summary>
    Review,

    /// <summary>A quiz on one study session's objectives.</summary>
    Quiz,

    /// <summary>A timed mock exam.</summary>
    MockExam,
}

/// <summary>One answer to one item. The history behind the schedule, and what readiness is measured from.</summary>
public sealed class ReviewLog
{
    private List<int> _selectedOptions = [];

    private ReviewLog()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid CertificationId { get; private set; }
    public PracticeItemKind Kind { get; private set; }
    public ReviewSource Source { get; private set; }
    public ReviewGrade Grade { get; private set; }

    /// <summary>Whether a question was answered correctly; null for flashcards, which the learner grades.</summary>
    public bool? Correct { get; private set; }

    public IReadOnlyList<int> SelectedOptions => _selectedOptions;

    /// <summary>The first review of the item, which counts against the daily limit of new items.</summary>
    public bool WasNew { get; private set; }

    /// <summary>The interval the review set, in days.</summary>
    public int IntervalDays { get; private set; }

    public DateTimeOffset ReviewedAt { get; private set; }

    /// <summary>The learner's calendar day, so "reviewed today" matches their time zone.</summary>
    public DateOnly ReviewedOn { get; private set; }

    internal static ReviewLog For(
        PracticeItem item, ReviewSource source, ReviewGrade grade, bool? correct, IReadOnlyList<int>? selected, bool wasNew, DateOnly today, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OwnerId = item.OwnerId,
            ItemId = item.Id,
            CertificationId = item.CertificationId,
            Kind = item.Kind,
            Source = source,
            Grade = grade,
            Correct = correct,
            _selectedOptions = [.. selected ?? []],
            WasNew = wasNew,
            IntervalDays = item.IntervalDays,
            ReviewedAt = now,
            ReviewedOn = today,
        };
}
