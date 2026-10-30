using System.Text.RegularExpressions;
using CertCoach.Application.Ai;
using CertCoach.Application.Ask;
using CertCoach.Application.Search;
using Microsoft.Extensions.DependencyInjection;

namespace CertCoach.Eval;

/// <param name="Rank">1-based rank of the first passage containing the evidence, or null if it is not in the top 10.</param>
internal sealed record QuestionResult(EvalQuestion Question, int? Rank, SearchHit? Top);

internal sealed record ModeResult(SearchMode Mode, IReadOnlyList<QuestionResult> Questions)
{
    public double RecallAt1 => Share(r => r.Rank == 1);
    public double RecallAt5 => Share(r => r.Rank <= 5);
    public double Mrr => Questions.Count == 0 ? 0 : Questions.Average(r => r.Rank is { } n ? 1.0 / n : 0);

    private double Share(Func<QuestionResult, bool> predicate) =>
        Questions.Count == 0 ? 0 : (double)Questions.Count(predicate) / Questions.Count;
}

/// <param name="BestSimilarity">The highest cosine similarity among the hybrid results, which the Ask gate compares with its threshold.</param>
internal sealed record GateResult(EvalQuestion Question, double BestSimilarity);

/// <param name="Unreachable">Answerable questions whose evidence no chunk contains in full, for example because a chunk boundary splits it.</param>
internal sealed record EvalResult(
    IReadOnlyList<ModeResult> Modes,
    IReadOnlyList<GateResult> Gate,
    double Threshold,
    IReadOnlyList<EvalQuestion> Unreachable,
    int ChunkCount,
    int DocumentCount);

internal static partial class Evaluator
{
    public const int TopK = 10;

    public static async Task<EvalResult> RunAsync(
        IServiceProvider services, IReadOnlyList<EvalQuestion> questions, IReadOnlyDictionary<string, Guid> documents)
    {
        await using var scope = services.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<SearchService>();
        var capabilities = scope.ServiceProvider.GetRequiredService<AiCapabilities>();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.CertCoachDbContext>();
        var chunks = db.DocumentChunks.Select(c => new { c.DocumentId, c.Text }).ToList();

        var answerable = questions.Where(q => !q.Unanswerable).ToList();
        var unreachable = answerable
            .Where(q => !chunks.Any(c => c.DocumentId == documents[q.Document!] && Contains(c.Text, q.Evidence!)))
            .ToList();
        var scored = answerable.Except(unreachable).ToList();

        var modes = new List<ModeResult>();
        foreach (var mode in new[] { SearchMode.Vector, SearchMode.Text, SearchMode.Hybrid })
        {
            var results = new List<QuestionResult>();
            foreach (var question in scored)
            {
                var hits = (await search.SearchAsync(new SearchRequest(question.Question, null, TopK, mode), CancellationToken.None)).Hits;
                var index = hits.ToList().FindIndex(h => h.DocumentId == documents[question.Document!] && Contains(h.Text, question.Evidence!));
                results.Add(new QuestionResult(question, index >= 0 ? index + 1 : null, hits.Count > 0 ? hits[0] : null));
            }

            modes.Add(new ModeResult(mode, results));
        }

        var gate = new List<GateResult>();
        foreach (var question in questions.Except(unreachable))
        {
            var hits = (await search.SearchAsync(new SearchRequest(question.Question, null, AskService.MaxSources), CancellationToken.None)).Hits;
            gate.Add(new GateResult(question, hits.Max(h => h.Similarity) ?? 0));
        }

        var threshold = capabilities.SemanticSearch ? AskService.MinSimilarity : AskService.MinSimilarityOffline;
        return new EvalResult(modes, gate, threshold, unreachable, chunks.Count, documents.Count);
    }

    /// <summary>Case- and whitespace-insensitive, since extraction rewraps lines.</summary>
    private static bool Contains(string text, string evidence) =>
        Normalize(text).Contains(Normalize(evidence), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
