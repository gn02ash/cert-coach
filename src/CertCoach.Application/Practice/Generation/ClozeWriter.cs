using System.Text.RegularExpressions;
using CertCoach.Application.Ask;
using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice.Generation;

/// <summary>
/// The offline writer: fill-in-the-blank items made from sentences in the learner's passages. The blank is the
/// sentence's most distinctive term (an acronym, a name, a rare word) that the objective itself doesn't give
/// away; wrong options are distinctive terms from the other sentences. No model, so nothing is made up.
/// </summary>
public static partial class ClozeWriter
{
    public const string Blank = "_____";

    private const int MinSentenceLength = 40;
    private const int MaxSentenceLength = 300;
    private const int MinWords = 7;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "because", "before", "being", "between", "both", "each", "every", "from", "have",
        "into", "more", "most", "only", "other", "over", "same", "should", "some", "such", "than", "that", "their",
        "them", "then", "there", "these", "they", "this", "those", "through", "under", "using", "very", "what", "when",
        "where", "which", "while", "with", "without", "would", "your", "does", "make", "makes", "used", "uses", "need",
        "needs", "first", "many", "much", "well", "just", "like", "within", "across", "against",
    };

    public static IReadOnlyList<ItemDraft> Write(ObjectiveContext objective, IReadOnlyList<AskSource> sources, int count)
    {
        var objectiveWords = Words(objective.Objective).Select(w => w.ToUpperInvariant()).ToHashSet();
        var sentences = sources
            .SelectMany(s => QuotedAnswer.Sentences(s.Text).Select(text => (Source: s, Text: text)))
            .Where(s => IsProse(s.Text))
            .ToList();

        // How many sentences each term appears in: rarer terms make better blanks.
        var frequency = sentences
            .SelectMany(s => Words(s.Text).Select(w => w.ToUpperInvariant()).Distinct())
            .CountBy(w => w)
            .ToDictionary(p => p.Key, p => p.Value);

        var candidates = sentences
            .Select(s => (s.Source, s.Text, Term: BestTerm(s.Text, objectiveWords, frequency)))
            .Where(c => c.Term is not null)
            .Select(c => (c.Source, c.Text, Term: c.Term!.Value.Term, Score: c.Term.Value.Score + 2 * Relevance(c.Text, objectiveWords)))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Source.Number)
            .DistinctBy(c => c.Term.ToUpperInvariant())
            .ToList();

        // Every distinctive word in the passages can be a wrong option, not only the ones chosen as blanks.
        var pool = sentences
            .SelectMany(s => Words(s.Text))
            .Where(w => IsDistinctive(w, objectiveWords))
            .DistinctBy(w => w.ToUpperInvariant())
            .ToList();

        var existing = objective.ExistingPrompts.Select(PracticeItem.FingerprintOf).ToHashSet();
        var mix = ItemMix.For(count);
        var drafts = new List<ItemDraft>();
        foreach (var c in candidates)
        {
            if (drafts.Count == count)
            {
                break;
            }

            // Both kinds share the prompt, so a sentence never comes back as a card after being a question.
            var stem = $"Fill in the blank: {BlankOut(c.Text, c.Term)}";
            var cardsSoFar = drafts.Count(d => d.Kind == PracticeItemKind.Flashcard);
            var distractors = Distractors(c.Term, c.Text, pool);
            var asCard = cardsSoFar < mix.Flashcards || distractors.Count < 3;

            var draft = asCard
                ? new ItemDraft(PracticeItemKind.Flashcard, stem, [], [], $"{c.Term}\n\n\"{c.Text}\"", [c.Source.Number], c.Text)
                : Question(stem, c.Term, distractors, c.Text, c.Source);
            if (!existing.Contains(PracticeItem.FingerprintOf(draft.Prompt)))
            {
                drafts.Add(draft);
            }
        }

        return drafts;
    }

    private static ItemDraft Question(string stem, string term, IReadOnlyList<string> distractors, string sentence, AskSource source)
    {
        // A fixed order per question, so the correct option isn't always first and regenerating is stable.
        var options = distractors.Take(3).Append(term)
            .OrderBy(o => StableHash(o + stem))
            .ThenBy(o => o, StringComparer.Ordinal)
            .ToList();
        var page = source.PageStart == source.PageEnd ? $"p. {source.PageStart}" : $"pp. {source.PageStart}-{source.PageEnd}";
        return new ItemDraft(
            PracticeItemKind.SingleChoice,
            stem,
            options,
            [options.IndexOf(term)],
            $"\"{term}\" completes the sentence in {source.DocumentTitle}, {page}: \"{sentence}\"",
            [source.Number],
            sentence);
    }

    /// <summary>
    /// Distinctive words from elsewhere in the passages, of the same shape (acronym, capitalized or plain) and
    /// close in length, so the answer doesn't stand out. Words from the sentence itself would be obviously wrong.
    /// </summary>
    private static List<string> Distractors(string term, string sentence, IReadOnlyList<string> pool)
    {
        var inSentence = Words(sentence).Select(w => w.ToUpperInvariant()).ToHashSet();
        return pool
            .Where(w => !inSentence.Contains(w.ToUpperInvariant()))
            .OrderBy(w => Shape(w) == Shape(term) ? 0 : 1)
            .ThenBy(w => Math.Abs(w.Length - term.Length))
            .ThenBy(w => w, StringComparer.Ordinal)
            .Take(3)
            .ToList();
    }

    /// <summary>
    /// A full sentence of prose: not a table fragment, an arrow mapping, or an all-caps label such as "EXAM TIP" or
    /// "ANSWER:", which make poor blanks.
    /// </summary>
    private static bool IsProse(string sentence) =>
        sentence.Length is >= MinSentenceLength and <= MaxSentenceLength
        && Words(sentence).Count() >= MinWords
        && !sentence.Contains('→', StringComparison.Ordinal) // "→" maps a cue to an answer, as in cheat sheets.
        && !ShoutedWords().IsMatch(sentence)
        && !Label().IsMatch(sentence);

    private static int Shape(string word) => IsAcronym(word) ? 2 : char.IsUpper(word[0]) ? 1 : 0;

    private static bool IsDistinctive(string word, HashSet<string> objectiveWords) =>
        !objectiveWords.Contains(word.ToUpperInvariant()) && !StopWords.Contains(word) && (word.Length >= 4 || IsAcronym(word));

    private static (string Term, double Score)? BestTerm(string sentence, HashSet<string> objectiveWords, Dictionary<string, int> frequency)
    {
        var words = Words(sentence).ToList();
        (string Term, double Score)? best = null;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var upper = word.ToUpperInvariant();
            if (!IsDistinctive(word, objectiveWords))
            {
                continue;
            }

            var score = 2.0 / frequency.GetValueOrDefault(upper, 1)
                + (IsAcronym(word) ? 3 : 0)
                + (i > 0 && char.IsUpper(word[0]) ? 2 : 0)
                + Math.Min(word.Length, 12) / 4.0;
            if (best is null || score > best.Value.Score)
            {
                best = (word, score);
            }
        }

        return best;
    }

    private static int Relevance(string sentence, HashSet<string> objectiveWords) =>
        Words(sentence).Select(w => w.ToUpperInvariant()).Distinct().Count(objectiveWords.Contains);

    private static string BlankOut(string sentence, string term) =>
        Regex.Replace(sentence, $@"(?<![\p{{L}}\p{{N}}.-]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}-]|\.\p{{L}})", Blank, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>FNV-1a. string.GetHashCode is randomized per process.</summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }

    private static bool IsAcronym(string word) => word.Length >= 2 && word.All(c => char.IsUpper(c) || char.IsDigit(c));

    private static IEnumerable<string> Words(string text) => Word().Matches(text).Select(m => m.Value);

    /// <summary>Hyphenated words and codes such as "AZ-104" stay whole, so a blank never leaves half of one behind.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}.-])\p{L}[\p{L}\p{N}]*(?:-[\p{L}\p{N}]+)*(?![\p{L}\p{N}-]|\.\p{L})")]
    private static partial Regex Word();

    [GeneratedRegex(@"\b[A-Z]{3,}\s+[A-Z]{3,}\b")]
    private static partial Regex ShoutedWords();

    [GeneratedRegex(@"^[A-Z][A-Z ]+:")]
    private static partial Regex Label();
}
