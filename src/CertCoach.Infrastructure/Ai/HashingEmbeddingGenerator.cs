using System.Text;
using CertCoach.Application.Ai;
using Microsoft.Extensions.AI;

namespace CertCoach.Infrastructure.Ai;

/// <summary>
/// Offline stand-in for an embedding model: words and word pairs are hashed into a fixed-size vector
/// (the "hashing trick"). It captures shared vocabulary, not meaning, which is enough for the app,
/// CI and the e2e test to run without API keys.
/// </summary>
internal sealed class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    /// <summary>Stored with every chunk; changing it makes the worker re-embed existing documents.</summary>
    public const string ModelId = "offline-hashing-v2";

    private const float PairWeight = 0.5f;

    /// <summary>
    /// Words that appear in almost every question and passage. Counting them made unrelated questions look
    /// similar to everything, so the "not in your materials" gate rarely fired (v1, measured by the eval tool).
    /// </summary>
    private static readonly HashSet<string> StopWords =
    [
        "a", "about", "after", "all", "also", "an", "and", "any", "are", "as", "at", "be", "because", "been", "before",
        "being", "between", "both", "but", "by", "can", "could", "did", "do", "does", "doing", "each", "for", "from",
        "had", "has", "have", "having", "how", "if", "in", "into", "is", "it", "its", "just", "may", "might", "more",
        "most", "much", "must", "my", "no", "not", "of", "on", "once", "only", "or", "other", "our", "out", "over",
        "same", "should", "so", "some", "such", "than", "that", "the", "their", "them", "then", "there", "these",
        "they", "this", "those", "through", "to", "too", "under", "until", "up", "very", "was", "we", "were", "what",
        "when", "where", "which", "while", "who", "whom", "why", "will", "with", "would", "you", "your",
    ];

    private readonly EmbeddingGeneratorMetadata _metadata =
        new("offline", defaultModelId: ModelId, defaultModelDimensions: AiCapabilities.EmbeddingDimensions);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(v => new Embedding<float>(Embed(v)) { ModelId = ModelId, CreatedAt = DateTimeOffset.UtcNow });
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    internal static float[] Embed(string text)
    {
        var vector = new float[AiCapabilities.EmbeddingDimensions];
        var words = Tokenize(text);

        for (var i = 0; i < words.Count; i++)
        {
            Add(vector, words[i], 1f);
            if (i > 0)
            {
                Add(vector, $"{words[i - 1]} {words[i]}", PairWeight);
            }
        }

        // Sublinear term frequency, so one repeated word does not dominate, then unit length for cosine.
        var norm = 0f;
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = MathF.Sign(vector[i]) * MathF.Log(1 + MathF.Abs(vector[i]));
            norm += vector[i] * vector[i];
        }

        if (norm > 0)
        {
            norm = MathF.Sqrt(norm);
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    private static void Add(float[] vector, string feature, float weight)
    {
        var hash = Fnv1a(feature);
        var index = (int)(hash % (uint)vector.Length);

        // A second bit of the hash picks the sign, so collisions cancel out on average instead of piling up.
        vector[index] += (hash & 0x8000_0000) == 0 ? weight : -weight;
    }

    private static List<string> Tokenize(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 1 && !StopWords.Contains(current.ToString()))
            {
                words.Add(Stem(current.ToString()));
            }

            current.Clear();
        }
    }

    /// <summary>Folds the most common English plural forms, so "embeddings" matches "embedding".</summary>
    private static string Stem(string word) => word switch
    {
        { Length: > 4 } when word.EndsWith("ies", StringComparison.Ordinal) => word[..^3] + "y",
        { Length: > 3 } when word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) => word[..^1],
        _ => word,
    };

    /// <summary>Stable across processes, unlike <see cref="string.GetHashCode()"/>, so stored vectors stay valid.</summary>
    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}
