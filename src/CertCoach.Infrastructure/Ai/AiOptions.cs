namespace CertCoach.Infrastructure.Ai;

public enum ChatProvider
{
    Offline,
    Anthropic,
    AzureOpenAI,
    OpenAICompatible,
}

public enum EmbeddingProvider
{
    Offline,
    AzureOpenAI,
    OpenAICompatible,
}

/// <summary>Bound from the <c>Ai</c> section. Keys come from environment variables or user secrets, never appsettings.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public ChatOptions Chat { get; set; } = new();
    public EmbeddingOptions Embeddings { get; set; } = new();

    public sealed class ChatOptions
    {
        public ChatProvider Provider { get; set; } = ChatProvider.Offline;

        /// <summary>Model ID, or the deployment name on Azure OpenAI.</summary>
        public string? Model { get; set; }

        public string? ApiKey { get; set; }

        /// <summary>Azure OpenAI resource URL, or the base URL of an OpenAI-compatible server.</summary>
        public string? Endpoint { get; set; }
    }

    public sealed class EmbeddingOptions
    {
        public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.Offline;

        /// <summary>Model ID, or the deployment name on Azure OpenAI.</summary>
        public string? Model { get; set; }

        public string? ApiKey { get; set; }
        public string? Endpoint { get; set; }

        /// <summary>
        /// Ask the server for <see cref="Application.Ai.AiCapabilities.EmbeddingDimensions"/> values.
        /// Turn off for models that reject the parameter but already return that size.
        /// </summary>
        public bool RequestDimensions { get; set; } = true;
    }

    public const string DefaultAnthropicModel = "claude-opus-5-5";
    public const string DefaultEmbeddingModel = "text-embedding-3-small";

    /// <summary>Returns every configuration problem at once, so startup fails with a complete list.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        switch (Chat.Provider)
        {
            case ChatProvider.Anthropic:
                Require(Chat.ApiKey, "Ai:Chat:ApiKey (ANTHROPIC_API_KEY)");
                break;
            case ChatProvider.AzureOpenAI:
                Require(Chat.Endpoint, "Ai:Chat:Endpoint");
                Require(Chat.ApiKey, "Ai:Chat:ApiKey");
                Require(Chat.Model, "Ai:Chat:Model (the deployment name)");
                break;
            case ChatProvider.OpenAICompatible:
                Require(Chat.Endpoint, "Ai:Chat:Endpoint");
                Require(Chat.Model, "Ai:Chat:Model");
                break;
        }

        switch (Embeddings.Provider)
        {
            case EmbeddingProvider.AzureOpenAI:
                Require(Embeddings.Endpoint, "Ai:Embeddings:Endpoint");
                Require(Embeddings.ApiKey, "Ai:Embeddings:ApiKey");
                Require(Embeddings.Model, "Ai:Embeddings:Model (the deployment name)");
                break;
            case EmbeddingProvider.OpenAICompatible:
                Require(Embeddings.Endpoint, "Ai:Embeddings:Endpoint");
                break;
        }

        foreach (var endpoint in new[] { Chat.Endpoint, Embeddings.Endpoint })
        {
            if (!string.IsNullOrWhiteSpace(endpoint) && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                errors.Add($"'{endpoint}' is not an absolute URL.");
            }
        }

        return errors;

        void Require(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{name} is required.");
            }
        }
    }
}
