using System.ClientModel;
using Anthropic;
using CertCoach.Application.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using BetaParams = Anthropic.Models.Beta.Messages;

namespace CertCoach.Infrastructure.Ai;

internal static class AiSetup
{
    /// <summary>Lets Claude re-serve a policy decline on the server's default fallback model instead of failing the answer.</summary>
    private const string ServerSideFallbackBeta = "server-side-fallback-2026-07-01";

    private const int DefaultMaxOutputTokens = 16_000;

    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();

        services.AddSingleton(sp => Describe(sp.GetRequiredService<IOptions<AiOptions>>().Value));

        // Clients are built on first use, so tooling that starts the host (OpenAPI generation) needs no keys.
        services.AddChatClient(sp => CreateChatClient(sp.GetRequiredService<IOptions<AiOptions>>().Value.Chat))
            .UseLogging();
        services.AddEmbeddingGenerator(sp => CreateEmbeddingGenerator(sp.GetRequiredService<IOptions<AiOptions>>().Value.Embeddings))
            .UseLogging();

        return services;
    }

    internal static AiCapabilities Describe(AiOptions options)
    {
        var chat = options.Chat;
        var embeddings = options.Embeddings;
        var chatModel = chat.Provider switch
        {
            ChatProvider.Offline => null,
            ChatProvider.Anthropic => chat.Model ?? AiOptions.DefaultAnthropicModel,
            _ => chat.Model,
        };
        var embeddingModel = embeddings.Provider == EmbeddingProvider.Offline
            ? HashingEmbeddingGenerator.ModelId
            : embeddings.Model ?? AiOptions.DefaultEmbeddingModel;

        return new AiCapabilities(
            chat.Provider.ToString(),
            chatModel,
            embeddings.Provider.ToString(),
            embeddingModel,
            ChatAvailable: chat.Provider != ChatProvider.Offline,
            SemanticSearch: embeddings.Provider != EmbeddingProvider.Offline);
    }

    private static IChatClient CreateChatClient(AiOptions.ChatOptions chat) => chat.Provider switch
    {
        ChatProvider.Anthropic => CreateAnthropicClient(chat.ApiKey!, chat.Model ?? AiOptions.DefaultAnthropicModel),
        ChatProvider.AzureOpenAI => OpenAIClientFor(chat.Endpoint!, chat.ApiKey, azure: true).GetChatClient(chat.Model).AsIChatClient(),
        ChatProvider.OpenAICompatible => OpenAIClientFor(chat.Endpoint!, chat.ApiKey, azure: false).GetChatClient(chat.Model).AsIChatClient(),
        _ => new UnavailableChatClient(),
    };

    /// <param name="httpClient">Lets tests inspect the request that goes over the wire.</param>
    internal static IChatClient CreateAnthropicClient(string apiKey, string model, HttpClient? httpClient = null)
    {
        var client = httpClient is null
            ? new AnthropicClient { ApiKey = apiKey }
            : new AnthropicClient { ApiKey = apiKey, HttpClient = httpClient };

        // The adapter adds the messages and remaining options to these caller-supplied parameters.
        return client.Beta.AsIChatClient(model)
            .AsBuilder()
            .ConfigureOptions(o =>
            {
                // Values set here win over ChatOptions, so carry the per-request ones across.
                var maxTokens = o.MaxOutputTokens ?? DefaultMaxOutputTokens;
                var requestModel = o.ModelId ?? model;
                o.RawRepresentationFactory ??= _ => new BetaParams.MessageCreateParams
                {
                    Model = requestModel,
                    Messages = [],
                    MaxTokens = maxTokens,
                    Fallbacks = new BetaParams.Default(),
                    Betas = [ServerSideFallbackBeta],
                };
            })
            .Build();
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiOptions.EmbeddingOptions embeddings)
    {
        var model = embeddings.Model ?? AiOptions.DefaultEmbeddingModel;
        IEmbeddingGenerator<string, Embedding<float>> generator = embeddings.Provider switch
        {
            EmbeddingProvider.AzureOpenAI => OpenAIClientFor(embeddings.Endpoint!, embeddings.ApiKey, azure: true)
                .GetEmbeddingClient(model).AsIEmbeddingGenerator(),
            EmbeddingProvider.OpenAICompatible => OpenAIClientFor(embeddings.Endpoint!, embeddings.ApiKey, azure: false)
                .GetEmbeddingClient(model).AsIEmbeddingGenerator(),
            _ => new HashingEmbeddingGenerator(),
        };

        return embeddings.Provider != EmbeddingProvider.Offline && embeddings.RequestDimensions
            ? generator.AsBuilder().ConfigureOptions(o => o.Dimensions ??= AiCapabilities.EmbeddingDimensions).Build()
            : generator;
    }

    /// <summary>
    /// Azure OpenAI's v1 API speaks the OpenAI protocol under <c>/openai/v1/</c>, so one SDK serves both,
    /// and the deployment name is passed as the model.
    /// </summary>
    internal static OpenAIClient OpenAIClientFor(string endpoint, string? apiKey, bool azure)
    {
        var url = endpoint.TrimEnd('/');
        if (azure && !url.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            url += "/openai/v1";
        }

        // Local servers such as Ollama ignore the key, but the SDK requires one.
        var key = string.IsNullOrWhiteSpace(apiKey) ? "unused" : apiKey;
        return new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(url + "/") });
    }

    /// <summary>Reports each missing setting by name rather than one generic failure.</summary>
    private sealed class AiOptionsValidator : IValidateOptions<AiOptions>
    {
        public ValidateOptionsResult Validate(string? name, AiOptions options) =>
            options.Validate() is { Count: > 0 } errors ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
