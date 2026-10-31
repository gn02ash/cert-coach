using System.Globalization;
using System.Text;
using CertCoach.Application.Ai;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Application.Search;

namespace CertCoach.Eval;

internal static class Report
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Generation(GenerationResult generation)
    {
        var md = new StringBuilder().AppendLine().AppendLine("## Question generation (offline)").AppendLine();
        md.AppendLine("Fill-in-the-blank items written from the corpus for each objective of the DEMO-101 sample outline, as a generation run does without a chat model. An item is grounded when its prompt is the quoted sentence with the answer blanked out and that sentence is in the passage it cites.").AppendLine();
        md.AppendLine("| Objectives | Covered by the corpus | Items | Of which questions | Grounded |");
        md.AppendLine("|---:|---:|---:|---:|---:|");
        md.AppendLine(Invariant, $"| {generation.Objectives} | {generation.Covered} | {generation.Items} | {generation.Questions} | {Percent(generation.GroundedShare)} |");
        foreach (var prompt in generation.Ungrounded)
        {
            md.AppendLine(Invariant, $"- Not grounded: {prompt}");
        }

        return md.ToString();
    }

    public static string Markdown(EvalResult result, AiCapabilities capabilities, ChunkingOptions chunking)
    {
        var md = new StringBuilder();
        var answerable = result.Gate.Count(g => !g.Question.Unanswerable);
        var unanswerable = result.Gate.Count(g => g.Question.Unanswerable);

        md.AppendLine("# Retrieval evaluation").AppendLine();
        md.AppendLine(Invariant, $"- **Embeddings:** `{capabilities.EmbeddingModel}` ({capabilities.EmbeddingProvider})");
        md.AppendLine(Invariant, $"- **Corpus:** {result.DocumentCount} synthetic PDFs, {result.ChunkCount} chunks of up to ~{chunking.TargetTokens} tokens");
        md.AppendLine(Invariant, $"- **Questions:** {answerable} answerable, {unanswerable} the corpus cannot answer");
        md.AppendLine();

        md.AppendLine("## Retrieval").AppendLine();
        md.AppendLine("A question counts as found when a returned passage from the right document contains its evidence phrase.").AppendLine();
        md.AppendLine("| Mode | Recall@1 | Recall@5 | MRR@10 |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var mode in result.Modes)
        {
            md.AppendLine(Invariant, $"| {mode.Mode} | {Percent(mode.RecallAt1)} | {Percent(mode.RecallAt5)} | {mode.Mrr:F2} |");
        }

        md.AppendLine();
        AppendGate(md, result);

        var hybridMisses = result.Modes.Single(m => m.Mode == SearchMode.Hybrid).Questions.Where(q => q.Rank is null or > 5).ToList();
        if (hybridMisses.Count > 0)
        {
            md.AppendLine("## Hybrid misses (not in the top 5)").AppendLine();
            foreach (var miss in hybridMisses)
            {
                var top = miss.Top is { } t ? (t.HeadingPath.Length > 0 ? t.HeadingPath : t.DocumentTitle) : "nothing";
                var rank = miss.Rank is { } r ? $"rank {r}" : "not in top 10";
                md.AppendLine(Invariant, $"- `{miss.Question.Id}` {miss.Question.Question} ({rank}; top result: {top})");
            }

            md.AppendLine();
        }

        if (result.Unreachable.Count > 0)
        {
            md.AppendLine("## Not scored").AppendLine();
            md.AppendLine("No chunk contains these questions' evidence in full (a chunk boundary splits it):").AppendLine();
            foreach (var question in result.Unreachable)
            {
                md.AppendLine(Invariant, $"- `{question.Id}` {question.Question}");
            }

            md.AppendLine();
        }

        return md.ToString();
    }

    private static void AppendGate(StringBuilder md, EvalResult result)
    {
        var answerable = result.Gate.Where(g => !g.Question.Unanswerable).Select(g => g.BestSimilarity).Order().ToList();
        var unanswerable = result.Gate.Where(g => g.Question.Unanswerable).Select(g => g.BestSimilarity).Order().ToList();
        var kept = answerable.Count(s => s >= result.Threshold);
        var declined = unanswerable.Count(s => s < result.Threshold);

        md.AppendLine("## \"Not in your materials\" gate").AppendLine();
        md.AppendLine(Invariant, $"Ask answers only when the best passage's cosine similarity is at least **{result.Threshold:F2}**; otherwise no model is called.").AppendLine();
        md.AppendLine("| | Result |");
        md.AppendLine("|---|---:|");
        md.AppendLine(Invariant, $"| Answerable questions answered | {kept}/{answerable.Count} ({Share(kept, answerable.Count)}) |");
        md.AppendLine(Invariant, $"| Unanswerable questions declined | {declined}/{unanswerable.Count} ({Share(declined, unanswerable.Count)}) |");
        md.AppendLine(Invariant, $"| Best similarity, answerable | min {Min(answerable)} · median {Median(answerable)} |");
        md.AppendLine(Invariant, $"| Best similarity, unanswerable | median {Median(unanswerable)} · max {Max(unanswerable)} |");

        // The threshold that would classify this set best, as a hint for tuning (it will overfit a small set).
        var candidates = answerable.Concat(unanswerable).Distinct().Order().ToList();
        if (candidates.Count > 0)
        {
            var best = candidates
                .Select(t => (Threshold: t, Correct: answerable.Count(s => s >= t) + unanswerable.Count(s => s < t)))
                .OrderByDescending(x => x.Correct)
                .ThenBy(x => Math.Abs(x.Threshold - result.Threshold))
                .First();
            md.AppendLine(Invariant, $"| Best threshold on this set | {best.Threshold:F2} ({best.Correct}/{answerable.Count + unanswerable.Count} correct) |");
        }

        md.AppendLine();
    }

    private static string Share(int part, int total) => total == 0 ? "n/a" : Percent((double)part / total);

    private static string Percent(double share) => (share * 100).ToString("F0", Invariant) + "%";

    private static string Min(List<double> sorted) => sorted.Count == 0 ? "n/a" : sorted[0].ToString("F2", Invariant);

    private static string Max(List<double> sorted) => sorted.Count == 0 ? "n/a" : sorted[^1].ToString("F2", Invariant);

    private static string Median(List<double> sorted) => sorted.Count == 0 ? "n/a" : sorted[sorted.Count / 2].ToString("F2", Invariant);
}
