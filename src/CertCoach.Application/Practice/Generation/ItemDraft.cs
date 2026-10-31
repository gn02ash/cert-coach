using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice.Generation;

/// <summary>What a writer needs to know about the objective it writes for.</summary>
/// <param name="ExistingPrompts">Items the objective already has, so a new run doesn't repeat them.</param>
public sealed record ObjectiveContext(
    Guid CertificationId,
    Guid ObjectiveId,
    string ExamCode,
    string CertificationName,
    string Domain,
    string SkillArea,
    string Objective,
    IReadOnlyList<string> ExistingPrompts);

/// <summary>A written item before validation.</summary>
/// <param name="SourceNumbers">1-based numbers of the passages it cites; empty when written from model knowledge.</param>
/// <param name="Quote">A sentence from a cited passage that supports the answer, when the writer gave one.</param>
public sealed record ItemDraft(
    PracticeItemKind Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    IReadOnlyList<int> CorrectOptions,
    string Answer,
    IReadOnlyList<int> SourceNumbers,
    string? Quote);

/// <summary>How a batch of items splits into kinds: mostly single-answer questions, as on the exam.</summary>
public readonly record struct ItemMix(int SingleChoice, int MultipleChoice, int Flashcards)
{
    public static ItemMix For(int count)
    {
        var flashcards = count >= 2 ? Math.Max(1, count / 3) : 0;
        var multiple = count >= 4 ? 1 : 0;
        return new ItemMix(count - flashcards - multiple, multiple, flashcards);
    }
}
