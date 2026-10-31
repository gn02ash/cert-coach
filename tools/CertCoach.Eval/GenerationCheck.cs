using System.Text.RegularExpressions;
using CertCoach.Application.Ask;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Practice.Generation;
using CertCoach.Application.Search;
using CertCoach.Domain.Practice;
using Microsoft.Extensions.DependencyInjection;

namespace CertCoach.Eval;

/// <param name="Objectives">Objectives in the sample outline.</param>
/// <param name="Covered">Objectives the corpus has passages for, past the relevance gate.</param>
/// <param name="Grounded">Items whose answer is in their quote and whose quote is in the passage they cite.</param>
internal sealed record GenerationResult(int Objectives, int Covered, int Items, int Grounded, int Questions, IReadOnlyList<string> Ungrounded)
{
    public double GroundedShare => Items == 0 ? 1 : (double)Grounded / Items;
}

/// <summary>
/// Writes offline fill-in-the-blank items for every objective of the DEMO-101 sample outline from the corpus, the
/// way a generation run does, and checks each one is grounded: the prompt is the quoted sentence with the answer
/// blanked out, and that sentence is in the passage the item cites.
/// </summary>
internal static class GenerationCheck
{
    public const int PerObjective = 5;

    public static async Task<GenerationResult> RunAsync(IServiceProvider services, string root)
    {
        var outline = OutlineParser.Parse(await File.ReadAllTextAsync(Path.Combine(root, "samples", "DEMO-101-outline.md")));
        var objectives = outline.Domains
            .SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives.Select(o => (Domain: d.Name, SkillArea: s.Name, Text: o))))
            .ToList();

        await using var scope = services.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<SearchService>();

        var (covered, items, grounded, questions) = (0, 0, 0, 0);
        var ungrounded = new List<string>();
        foreach (var objective in objectives)
        {
            var result = await search.SearchAsync(new SearchRequest(objective.Text, Limit: ItemGenerator.PassagesPerObjective), CancellationToken.None);
            var threshold = result.Semantic ? AskService.MinSimilarity : AskService.MinSimilarityOffline;
            if (result.Hits.Max(h => h.Similarity) is not { } best || best < threshold)
            {
                continue;
            }

            covered++;
            var passages = result.Hits
                .Select((h, i) => new AskSource(i + 1, h.ChunkId, h.DocumentId, h.DocumentTitle, h.HeadingPath, h.PageStart, h.PageEnd, h.Text))
                .ToList();
            var context = new ObjectiveContext(Guid.Empty, Guid.Empty, "DEMO-101", "Sample", objective.Domain, objective.SkillArea, objective.Text, []);

            foreach (var draft in ClozeWriter.Write(context, passages, PerObjective))
            {
                items++;
                questions += draft.Kind == PracticeItemKind.Flashcard ? 0 : 1;
                if (IsGrounded(draft, passages))
                {
                    grounded++;
                }
                else
                {
                    ungrounded.Add(draft.Prompt);
                }
            }
        }

        return new GenerationResult(objectives.Count, covered, items, grounded, questions, ungrounded);
    }

    internal static bool IsGrounded(ItemDraft draft, IReadOnlyList<AskSource> passages)
    {
        if (draft.Quote is not { } quote || draft.SourceNumbers is not [var number] || number < 1 || number > passages.Count)
        {
            return false;
        }

        var answer = draft.Kind == PracticeItemKind.Flashcard ? draft.Answer.Split('\n')[0] : draft.Options[draft.CorrectOptions[0]];
        var stem = draft.Prompt.Replace("Fill in the blank: ", "", StringComparison.Ordinal);
        var refilled = Regex.Replace(stem, Regex.Escape(ClozeWriter.Blank), answer.Replace("$", "$$", StringComparison.Ordinal));

        return Collapse(passages[number - 1].Text).Contains(Collapse(quote), StringComparison.Ordinal)
            && string.Equals(refilled, quote, StringComparison.OrdinalIgnoreCase);
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
