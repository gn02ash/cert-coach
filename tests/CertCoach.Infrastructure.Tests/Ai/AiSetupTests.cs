using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CertCoach.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace CertCoach.Infrastructure.Tests.Ai;

public class AiSetupTests
{
    [Fact]
    public void Offline_is_the_default_and_needs_no_settings()
    {
        var options = new AiOptions();

        options.Validate().ShouldBeEmpty();
        var capabilities = AiSetup.Describe(options);
        capabilities.ChatAvailable.ShouldBeFalse();
        capabilities.SemanticSearch.ShouldBeFalse();
        capabilities.EmbeddingModel.ShouldBe(HashingEmbeddingGenerator.ModelId);
    }

    [Fact]
    public void Lists_every_missing_setting_by_name()
    {
        var options = new AiOptions
        {
            Chat = { Provider = ChatProvider.Anthropic },
            Embeddings = { Provider = EmbeddingProvider.AzureOpenAI, Endpoint = "not a url" },
        };

        options.Validate().ShouldBe(
        [
            "Ai:Chat:ApiKey (ANTHROPIC_API_KEY) is required.",
            "Ai:Embeddings:ApiKey is required.",
            "Ai:Embeddings:Model (the deployment name) is required.",
            "'not a url' is not an absolute URL.",
        ]);
    }

    [Fact]
    public void Anthropic_defaults_to_the_current_opus_model()
    {
        var capabilities = AiSetup.Describe(new AiOptions { Chat = { Provider = ChatProvider.Anthropic, ApiKey = "k" } });

        capabilities.ChatAvailable.ShouldBeTrue();
        capabilities.ChatModel.ShouldBe(AiOptions.DefaultAnthropicModel);
    }

    [Fact]
    public async Task Anthropic_requests_opt_into_server_side_refusal_fallback()
    {
        var handler = new CapturingHandler();
        using var client = AiSetup.CreateAnthropicClient("test-key", "claude-opus-5-5", new HttpClient(handler));

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "Be brief."), new ChatMessage(ChatRole.User, "Hello")],
            new ChatOptions { MaxOutputTokens = 500 },
            TestContext.Current.CancellationToken);

        response.Text.ShouldBe("Hi");
        var body = JsonNode.Parse(handler.Body!)!;
        body["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
        body["fallbacks"]!.GetValue<string>().ShouldBe("default");
        body["max_tokens"]!.GetValue<int>().ShouldBe(500);
        body["messages"]!.AsArray().Count.ShouldBe(1);
        body["messages"]![0]!.ToJsonString().ShouldContain("Hello");
        body["system"]!.ToJsonString().ShouldContain("Be brief.");
        handler.BetaHeader.ShouldContain("server-side-fallback-2026-07-01");
    }

    [Fact]
    public async Task Anthropic_requests_carry_the_agents_tools_and_output_schema()
    {
        var handler = new CapturingHandler();
        using var client = AiSetup.CreateAnthropicClient("test-key", "claude-opus-5-5", new HttpClient(handler));
        var tool = AIFunctionFactory.Create((string url) => url, "fetch_page", "Fetches a page.");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Find the outline")],
            new ChatOptions { Tools = [tool], ResponseFormat = ChatResponseFormat.ForJsonSchema<Answer>() },
            TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(handler.Body!)!;
        body["tools"]!.AsArray().ShouldHaveSingleItem()!["name"]!.GetValue<string>().ShouldBe("fetch_page");
        var format = body["output_config"]!["format"]!;
        format["type"]!.GetValue<string>().ShouldBe("json_schema");
        format["schema"]!["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
        body["max_tokens"]!.GetValue<int>().ShouldBe(16_000);
    }

    private sealed record Answer(string Name, List<string> Items);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string BetaHeader { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            BetaHeader = request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "";

            const string message = """
                {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
                 "content":[{"type":"text","text":"Hi"}],"stop_reason":"end_turn","stop_sequence":null,
                 "usage":{"input_tokens":3,"output_tokens":1}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(message, Encoding.UTF8, "application/json") };
        }
    }
}
