using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice.Exams;

/// <summary>Start an exam for a certification, or for a plan's mock exam session, which is completed on submit.</summary>
public sealed record StartMockExamRequest(Guid? CertificationId, Guid? SessionId = null, int QuestionCount = 40, int DurationMinutes = 60);

public sealed record SaveMockAnswerRequest(IReadOnlyList<int> Selected);

/// <param name="RemainingSeconds">From the server's clock, so a wrong clock on the device can't change the time.</param>
/// <param name="Result">Set once submitted, together with each question's answer.</param>
public sealed record MockExamView(
    Guid Id,
    Guid CertificationId,
    string ExamCode,
    MockExamStatus Status,
    int DurationMinutes,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    int RemainingSeconds,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<MockQuestionView> Questions,
    MockExamResult? Result);

/// <param name="CorrectOptions">Null until the exam is submitted, as are <paramref name="Correct"/>, the answer and sources.</param>
public sealed record MockQuestionView(
    Guid ItemId,
    string? Domain,
    PracticeItemKind Kind,
    PracticeItemOrigin Origin,
    string Prompt,
    IReadOnlyList<string> Options,
    int ChooseCount,
    IReadOnlyList<int> Selected,
    IReadOnlyList<int>? CorrectOptions,
    bool? Correct,
    string? Answer,
    IReadOnlyList<ItemSourceView>? Sources);

public sealed record MockExamResult(int Correct, int Total, int Percent, IReadOnlyList<DomainScore> Domains);

public sealed record DomainScore(Guid DomainId, string Name, decimal Weight, int Correct, int Total);

/// <param name="Percent">Null while the exam is in progress.</param>
public sealed record MockExamSummary(Guid Id, MockExamStatus Status, DateTimeOffset StartedAt, DateTimeOffset Deadline, int Questions, int? Percent);
