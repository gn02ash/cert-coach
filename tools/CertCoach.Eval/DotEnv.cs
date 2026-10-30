using System.Collections;

namespace CertCoach.Eval;

/// <summary>
/// Reads the same AI_* variables Docker Compose uses (from .env or the environment) and maps them to the
/// app's Ai:* settings, so the eval runs with exactly the providers the app is configured with.
/// </summary>
internal static class DotEnv
{
    private static readonly Dictionary<string, string> Mapping = new(StringComparer.Ordinal)
    {
        ["AI_EMBEDDINGS_PROVIDER"] = "Ai:Embeddings:Provider",
        ["AI_EMBEDDINGS_MODEL"] = "Ai:Embeddings:Model",
        ["AI_EMBEDDINGS_API_KEY"] = "Ai:Embeddings:ApiKey",
        ["AI_EMBEDDINGS_ENDPOINT"] = "Ai:Embeddings:Endpoint",
    };

    public static Dictionary<string, string?> AiSettings(string envFile)
    {
        if (!File.Exists(envFile))
        {
            return [];
        }

        var values = File.ReadLines(envFile)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('=', StringComparison.Ordinal))
            .Select(l => (Key: l[..l.IndexOf('=', StringComparison.Ordinal)].Trim(), Value: l[(l.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim().Trim('"')));
        return Map(values);
    }

    public static Dictionary<string, string?> AiSettings(IDictionary environment) =>
        Map(environment.Keys.Cast<string>().Select(k => (k, environment[k]?.ToString() ?? "")));

    private static Dictionary<string, string?> Map(IEnumerable<(string Key, string Value)> values) =>
        values
            .Where(v => Mapping.ContainsKey(v.Key) && v.Value.Length > 0)
            .ToDictionary(v => Mapping[v.Key], v => (string?)v.Value);
}
