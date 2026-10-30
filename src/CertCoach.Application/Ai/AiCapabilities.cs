namespace CertCoach.Application.Ai;

/// <summary>Which AI providers this instance runs with, so features can degrade instead of failing.</summary>
/// <param name="ChatAvailable">False in offline mode: answers are quoted passages and AI parsing is hidden.</param>
/// <param name="SemanticSearch">False when embeddings are the offline word-hashing fallback.</param>
public sealed record AiCapabilities(
    string ChatProvider,
    string? ChatModel,
    string EmbeddingProvider,
    string EmbeddingModel,
    bool ChatAvailable,
    bool SemanticSearch)
{
    /// <summary>
    /// Every embedding is stored at this size. text-embedding-3 models are asked for it directly,
    /// so switching between small and large needs a re-embed but no migration.
    /// </summary>
    public const int EmbeddingDimensions = 1536;
}
