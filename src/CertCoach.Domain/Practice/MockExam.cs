using CertCoach.Domain.Common;

namespace CertCoach.Domain.Practice;

public enum MockExamStatus
{
    InProgress,
    Submitted,
}

/// <summary>The options the learner chose for one question, saved as they go.</summary>
public sealed record MockAnswer(Guid ItemId, IReadOnlyList<int> Selected);

/// <summary>
/// A timed exam of questions drawn in proportion to the exam's domain weights. Answers are saved as the learner
/// goes, with no feedback until it is submitted, by the learner or by the clock.
/// </summary>
public sealed class MockExam
{
    public const int MinQuestions = 5;
    public const int MaxQuestions = 100;
    public const int MinMinutes = 5;
    public const int MaxMinutes = 240;

    /// <summary>Answers saved this long after the deadline still count: the request may have been in flight.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private List<Guid> _itemIds = [];
    private List<MockAnswer> _answers = [];

    private MockExam()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CertificationId { get; private set; }

    /// <summary>The plan session this exam was taken for, completed when the exam is submitted.</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>The questions, in the order shown.</summary>
    public IReadOnlyList<Guid> ItemIds => _itemIds;

    public IReadOnlyList<MockAnswer> Answers => _answers;
    public int DurationMinutes { get; private set; }
    public MockExamStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public int? CorrectCount { get; private set; }

    public static MockExam Start(
        Guid ownerId, Guid certificationId, Guid? sessionId, IReadOnlyList<Guid> itemIds, int durationMinutes, DateTimeOffset now)
    {
        if (itemIds.Count < MinQuestions)
        {
            throw new DomainException(
                $"A mock exam needs at least {MinQuestions} questions, and this certification has {itemIds.Count}. Generate more on its Practice items page.");
        }

        if (itemIds.Count > MaxQuestions)
        {
            throw new DomainException($"A mock exam has at most {MaxQuestions} questions.");
        }

        if (durationMinutes is < MinMinutes or > MaxMinutes)
        {
            throw new DomainException($"Give the exam {MinMinutes} to {MaxMinutes} minutes.");
        }

        return new MockExam
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CertificationId = certificationId,
            SessionId = sessionId,
            _itemIds = itemIds.Distinct().ToList(),
            DurationMinutes = durationMinutes,
            Status = MockExamStatus.InProgress,
            StartedAt = now,
            Deadline = now.AddMinutes(durationMinutes),
        };
    }

    /// <summary>True once the time is up, whether or not the exam has been submitted yet.</summary>
    public bool IsOver(DateTimeOffset now) => Status == MockExamStatus.Submitted || now > Deadline;

    public IReadOnlyList<int> SelectedFor(Guid itemId) => _answers.Find(a => a.ItemId == itemId)?.Selected ?? [];

    public void SaveAnswer(Guid itemId, IReadOnlyList<int> selected, DateTimeOffset now)
    {
        if (Status == MockExamStatus.Submitted)
        {
            throw new DomainException("This exam has been submitted.");
        }

        if (now > Deadline + Grace)
        {
            throw new DomainException("Time is up for this exam.");
        }

        if (!_itemIds.Contains(itemId))
        {
            throw new DomainException("That question isn't part of this exam.");
        }

        // A new list, so EF Core sees the change to the JSON column.
        _answers = [.. _answers.Where(a => a.ItemId != itemId), new MockAnswer(itemId, selected.Distinct().Order().ToList())];
    }

    /// <summary>Marks the exam submitted with its score; grading the items is the caller's job.</summary>
    public void Submit(int correct, DateTimeOffset now)
    {
        if (Status == MockExamStatus.Submitted)
        {
            throw new DomainException("This exam has already been submitted.");
        }

        Status = MockExamStatus.Submitted;
        SubmittedAt = now < Deadline ? now : Deadline;
        CorrectCount = correct;
    }
}
