using System.Globalization;
using System.Text;
using CertCoach.Domain.Common;

namespace CertCoach.Domain.Practice;

public enum PracticeItemKind
{
    /// <summary>One correct option.</summary>
    SingleChoice,

    /// <summary>"Choose two" or more: every correct option must be selected.</summary>
    MultipleChoice,

    /// <summary>A front and a back, graded by the learner.</summary>
    Flashcard,
}

public enum PracticeItemOrigin
{
    /// <summary>Written from the learner's documents, with citations.</summary>
    Documents,

    /// <summary>Written by the model from its own knowledge because no document covered the objective. Shown as such.</summary>
    ModelKnowledge,

    /// <summary>Written by the learner.</summary>
    Manual,
}

public enum PracticeItemStatus
{
    Active,

    /// <summary>Kept, with its history, but left out of reviews and exams.</summary>
    Suspended,
}

/// <summary>Where an item's answer comes from. Kept when the chunk is re-processed, so the page link survives.</summary>
public sealed record ItemSource(Guid DocumentId, Guid? ChunkId, int Page, string? Quote);

/// <summary>
/// A practice question or flashcard for one objective, with its spaced-repetition schedule. A new item has no
/// due date; it is scheduled when first reviewed.
/// </summary>
public sealed class PracticeItem
{
    public const int MaxPromptLength = 2_000;
    public const int MaxAnswerLength = 4_000;
    public const int MaxOptionLength = 500;
    public const int MaxQuoteLength = 500;
    public const int MinOptions = 3;
    public const int MaxOptions = 8;
    public const double InitialEaseFactor = 2.5;

    private List<string> _options = [];
    private List<int> _correctOptions = [];
    private List<ItemSource> _sources = [];

    private PracticeItem()
    {
        Prompt = null!;
        Answer = null!;
        Fingerprint = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CertificationId { get; private set; }

    /// <summary>Null when the outline changed and no objective with the same text remains.</summary>
    public Guid? ObjectiveId { get; private set; }

    public PracticeItemKind Kind { get; private set; }
    public PracticeItemOrigin Origin { get; private set; }
    public PracticeItemStatus Status { get; private set; }

    /// <summary>The question, or the front of a flashcard.</summary>
    public string Prompt { get; private set; }

    /// <summary>The back of a flashcard, or why the correct options are right.</summary>
    public string Answer { get; private set; }

    /// <summary>Empty for flashcards.</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>Zero-based indexes into <see cref="Options"/>, ascending.</summary>
    public IReadOnlyList<int> CorrectOptions => _correctOptions;

    public IReadOnlyList<ItemSource> Sources => _sources;

    /// <summary>The prompt reduced to lowercase letters and digits, to spot the same question generated twice.</summary>
    public string Fingerprint { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Spaced repetition (SM-2).
    public double EaseFactor { get; private set; }
    public int IntervalDays { get; private set; }
    public int Repetitions { get; private set; }
    public int Lapses { get; private set; }

    /// <summary>Null until the first review.</summary>
    public DateOnly? DueOn { get; private set; }

    public DateTimeOffset? LastReviewedAt { get; private set; }

    public bool IsNew => LastReviewedAt is null;

    public static PracticeItem Create(
        Guid ownerId,
        Guid certificationId,
        Guid? objectiveId,
        PracticeItemKind kind,
        PracticeItemOrigin origin,
        string prompt,
        IReadOnlyList<string>? options,
        IReadOnlyList<int>? correctOptions,
        string answer,
        IReadOnlyList<ItemSource>? sources,
        DateTimeOffset now)
    {
        if (origin == PracticeItemOrigin.Documents && (sources is null || sources.Count == 0))
        {
            throw new DomainException("An item written from documents must cite at least one.");
        }

        var item = new PracticeItem
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CertificationId = certificationId,
            ObjectiveId = objectiveId,
            Kind = kind,
            Origin = origin,
            Status = PracticeItemStatus.Active,
            CreatedAt = now,
            EaseFactor = InitialEaseFactor,
        };
        item.Edit(prompt, options, correctOptions, answer);
        item._sources = (sources ?? [])
            .Select(s => s with { Quote = Truncate(s.Quote?.Trim(), MaxQuoteLength) })
            .ToList();
        return item;
    }

    /// <summary>Changes the content. The schedule is kept: fixing a typo shouldn't reset what was learned.</summary>
    public void Edit(string prompt, IReadOnlyList<string>? options, IReadOnlyList<int>? correctOptions, string answer)
    {
        prompt = Text(prompt, "The question", MaxPromptLength);
        answer = Kind == PracticeItemKind.Flashcard
            ? Text(answer, "The back of the card", MaxAnswerLength)
            : (answer ?? "").Trim();
        if (answer.Length > MaxAnswerLength)
        {
            throw new DomainException($"The explanation is longer than {MaxAnswerLength:N0} characters.");
        }

        var cleanOptions = (options ?? []).Select(o => Text(o, "An option", MaxOptionLength)).ToList();
        var correct = (correctOptions ?? []).Distinct().Order().ToList();

        if (Kind == PracticeItemKind.Flashcard)
        {
            if (cleanOptions.Count > 0 || correct.Count > 0)
            {
                throw new DomainException("A flashcard has no options.");
            }
        }
        else
        {
            if (cleanOptions.Count is < MinOptions or > MaxOptions)
            {
                throw new DomainException($"A question needs {MinOptions} to {MaxOptions} options.");
            }

            if (cleanOptions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cleanOptions.Count)
            {
                throw new DomainException("Two options are the same.");
            }

            if (correct.Any(i => i < 0 || i >= cleanOptions.Count))
            {
                throw new DomainException("A correct answer points to an option that doesn't exist.");
            }

            var (min, max) = Kind == PracticeItemKind.SingleChoice ? (1, 1) : (2, cleanOptions.Count - 1);
            if (correct.Count < min || correct.Count > max)
            {
                throw new DomainException(Kind == PracticeItemKind.SingleChoice
                    ? "A single-answer question needs exactly one correct option."
                    : "A multiple-answer question needs at least two correct options and at least one wrong one.");
            }
        }

        Prompt = prompt;
        Answer = answer;
        _options = cleanOptions;
        _correctOptions = correct;
        Fingerprint = FingerprintOf(prompt);
    }

    public void SetStatus(PracticeItemStatus status) => Status = status;

    /// <summary>Records a flashcard review graded by the learner and reschedules the card.</summary>
    public ReviewLog Grade(ReviewGrade grade, ReviewSource source, DateOnly today, DateTimeOffset now)
    {
        if (Kind != PracticeItemKind.Flashcard)
        {
            throw new DomainException("Questions are graded by the answer, not by the learner.");
        }

        return Reschedule(grade, correct: null, selected: null, source, today, now);
    }

    /// <summary>Checks an answer to a question and reschedules it: correct counts as Good, wrong as Again.</summary>
    public ReviewLog Submit(IReadOnlyList<int> selected, ReviewSource source, DateOnly today, DateTimeOffset now)
    {
        if (Kind == PracticeItemKind.Flashcard)
        {
            throw new DomainException("A flashcard has no options to choose.");
        }

        if (selected.Count == 0 || selected.Any(i => i < 0 || i >= _options.Count))
        {
            throw new DomainException("Choose one of the options.");
        }

        var correct = IsCorrect(selected);
        return Reschedule(correct ? ReviewGrade.Good : ReviewGrade.Again, correct, selected, source, today, now);
    }

    /// <summary>The interval each grade would set, for the buttons of a flashcard.</summary>
    public int IntervalAfter(ReviewGrade grade) => Sm2.Next(State, grade).IntervalDays;

    private Sm2State State => new(EaseFactor, IntervalDays, Repetitions, Lapses);

    private ReviewLog Reschedule(ReviewGrade grade, bool? correct, IReadOnlyList<int>? selected, ReviewSource source, DateOnly today, DateTimeOffset now)
    {
        var wasNew = IsNew;
        var next = Sm2.Next(State, grade);
        (EaseFactor, IntervalDays, Repetitions, Lapses) = (next.EaseFactor, next.IntervalDays, next.Repetitions, next.Lapses);
        DueOn = today.AddDays(next.IntervalDays);
        LastReviewedAt = now;
        return ReviewLog.For(this, source, grade, correct, selected, wasNew, today, now);
    }

    /// <summary>After the outline is replaced, moves the item to the objective with the same text, or to none.</summary>
    public void MoveToObjective(Guid? objectiveId) => ObjectiveId = objectiveId;

    /// <summary>True when the selected options are exactly the correct ones.</summary>
    public bool IsCorrect(IReadOnlyCollection<int> selected) =>
        Kind != PracticeItemKind.Flashcard && selected.Distinct().Order().SequenceEqual(_correctOptions);

    public static string FingerprintOf(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
        }

        return Truncate(builder.ToString(), 200)!;
    }

    private static string Text(string value, string what, int maxLength)
    {
        value = value?.Trim() ?? "";
        if (value.Length == 0)
        {
            throw new DomainException($"{what} is empty.");
        }

        return value.Length > maxLength
            ? throw new DomainException($"{what} is longer than {maxLength:N0} characters.")
            : value;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
