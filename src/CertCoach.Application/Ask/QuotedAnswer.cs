using System.Text.RegularExpressions;
using CertCoach.Application.Documents.Ingestion;

namespace CertCoach.Application.Ask;

/// <summary>
/// The offline answer: the sentences from the top sources that share the most words with the question,
/// quoted with their citations. No model, so nothing is made up, but nothing is explained either.
/// </summary>
public static partial class QuotedAnswer
{
    public const int MaxQuotes = 3;

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "are", "but", "not", "you", "your", "with", "how", "what", "when", "where", "which",
        "who", "why", "does", "this", "that", "from", "into", "can", "should", "would", "about", "there", "their",
        "them", "its", "have", "has", "was", "were", "will", "use", "used", "using",
    ];

    public static string Build(string question, IReadOnlyList<AskSource> sources)
    {
        var terms = Terms(question);
        var quotes = sources
            .SelectMany(s => Sentences(s.Text).Select((sentence, i) => (s.Number, Sentence: sentence, Position: i)))
            .Select(q => (q.Number, q.Sentence, q.Position, Score: Terms(q.Sentence).Count(terms.Contains)))
            .Where(q => q.Score > 0)
            .OrderByDescending(q => q.Score)
            .ThenBy(q => q.Number)
            .ThenBy(q => q.Position)
            .Take(MaxQuotes)
            // Back in reading order, so quotes from one source read naturally.
            .OrderBy(q => q.Number)
            .ThenBy(q => q.Position)
            .Select(q => $"- \"{q.Sentence}\" [{q.Number}]")
            .ToList();

        return quotes.Count == 0
            ? "These passages look related, but no sentence shares words with your question. Open them to check."
            : "From your materials:\n\n" + string.Join('\n', quotes);
    }

    /// <summary>
    /// Whole sentences only. Chunk text keeps subheadings on their own lines, so paragraphs are split first and
    /// lines without closing punctuation (headings) are skipped; otherwise a heading and the paragraph after it
    /// would be quoted as one "sentence".
    /// </summary>
    internal static IEnumerable<string> Sentences(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(Chunker.Sentences)
            .Where(s => SentenceEnd().IsMatch(s));

    private static HashSet<string> Terms(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Select(w => w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1] : w)
            .ToHashSet();

    [GeneratedRegex(@"[.!?][""')\]]?$")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[\p{L}\p{N}]{3,}")]
    private static partial Regex Word();
}
