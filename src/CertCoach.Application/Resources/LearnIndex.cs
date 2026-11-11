using System.Text.RegularExpressions;

namespace CertCoach.Application.Resources;

/// <summary>A Microsoft Learn training module, from Microsoft's public catalogue.</summary>
public sealed record LearnModule(string Uid, string Title, string Summary, Uri Url, int? DurationMinutes, IReadOnlyList<string> Levels);

/// <param name="Relevance">0–1: how much of the objective's distinctive wording the module covers.</param>
public sealed record LearnMatch(LearnModule Module, double Relevance);

/// <summary>
/// Finds the Learn modules that cover an objective by the words they share. A word counts for more the rarer it
/// is across the catalogue, and twice when it is in the module's title. Built once per catalogue download.
/// </summary>
public sealed partial class LearnIndex
{
    /// <summary>Below this, the module shares too little with the objective to be worth suggesting.</summary>
    public const double MinRelevance = 0.45;

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "that", "this", "from", "into", "your", "you", "how", "use", "using", "used", "are", "can",
        "implement", "configure", "manage", "create", "plan", "design", "describe", "identify", "select", "appropriate", "solution", "solutions",
    ];

    private readonly List<(LearnModule Module, HashSet<string> Title, HashSet<string> Summary)> _modules;
    private readonly Dictionary<string, double> _idf;

    public LearnIndex(IReadOnlyList<LearnModule> modules)
    {
        _modules = modules.Select(m => (m, Terms(m.Title), Terms(m.Summary))).ToList();
        var count = Math.Max(1, _modules.Count);
        _idf = _modules
            .SelectMany(m => m.Title.Union(m.Summary))
            .CountBy(t => t)
            .ToDictionary(p => p.Key, p => Math.Log(1 + ((double)count / p.Value)));
    }

    public int Count => _modules.Count;

    public IReadOnlyList<LearnMatch> Search(string objective, int limit)
    {
        // A word no module uses can't be matched, but still counts against the modules that lack it.
        var terms = Terms(objective).Select(t => (Term: t, Weight: _idf.GetValueOrDefault(t, Math.Log(1 + _modules.Count)))).ToList();
        var total = terms.Sum(t => t.Weight);
        if (total == 0)
        {
            return [];
        }

        return _modules
            .Select(m => new LearnMatch(m.Module, terms.Sum(t => m.Title.Contains(t.Term) ? t.Weight : m.Summary.Contains(t.Term) ? t.Weight * 0.5 : 0) / total))
            .Where(m => m.Relevance >= MinRelevance)
            .OrderByDescending(m => m.Relevance)
            .ThenBy(m => m.Module.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>Lower-case words of three letters or more, without stop words, with a plural "s" removed.</summary>
    internal static HashSet<string> Terms(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Select(w => w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1] : w)
            .ToHashSet();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}#+.]{2,}")]
    private static partial Regex Word();
}
