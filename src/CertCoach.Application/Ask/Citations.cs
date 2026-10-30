using System.Text.RegularExpressions;

namespace CertCoach.Application.Ask;

/// <summary>Finds the [n] source markers in an answer and keeps only those that point at a real source.</summary>
public static partial class Citations
{
    /// <returns>The distinct valid source numbers, in order of first citation.</returns>
    public static IReadOnlyList<int> Cited(string answer, int sourceCount) =>
        Marker().Matches(answer)
            .SelectMany(m => m.Groups["n"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(n => int.TryParse(n, out var number) ? number : 0)
            .Where(n => n >= 1 && n <= sourceCount)
            .Distinct()
            .ToList();

    // [3], [1, 4] or [1,4]; [3][4] is two markers.
    [GeneratedRegex(@"\[(?<n>\d{1,3}(?:\s*,\s*\d{1,3})*)\]")]
    private static partial Regex Marker();
}
