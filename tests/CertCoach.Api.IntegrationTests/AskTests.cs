using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CertCoach.Application.Ai;
using CertCoach.Application.Ask;
using CertCoach.Application.Documents;
using CertCoach.Domain.Documents;
using CertCoach.Infrastructure.Tests.Documents;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Api.IntegrationTests;

/// <summary>The answer stream end to end: offline quotes, the not-covered gate, and a model-written answer from a fake chat client.</summary>
public class AskTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Notes() => new TestPdf()
        .Page(("Retrieval notes", TitleSize), ("Hybrid search", HeadingSize),
            ("Hybrid search runs vector search and keyword search. It merges both rankings with reciprocal rank fusion.", BodySize))
        .Page(("Prompt caching", HeadingSize),
            ("Prompt caching reuses a long system prompt across requests. It lowers latency and cost.", BodySize))
        .Build();

    [Fact]
    public async Task Offline_answers_quote_the_matching_sentences_with_citations()
    {
        var client = await factory.CreateUserClientAsync();
        await IngestAsync(client, Notes());

        var events = await AskAsync(client, new AskRequest("How does hybrid search merge rankings?"));

        var sources = events.Sources();
        sources[0].HeadingPath.ShouldBe("Retrieval notes › Hybrid search");
        events.Answer().ShouldContain("\"It merges both rankings with reciprocal rank fusion.\" [1]");
        var done = events.Done();
        done.Kind.ShouldBe(AnswerKind.Quoted);
        done.Cited.ShouldContain(1);
        done.Notice.ShouldNotBeNull().ShouldContain("Offline mode");
    }

    [Fact]
    public async Task A_question_the_documents_do_not_cover_gets_no_sources_and_no_model_call()
    {
        var chat = new FakeChatClient("should not be called");
        var client = await CreateClientWithChatAsync(chat);
        await IngestAsync(client, Notes());

        var events = await AskAsync(client, new AskRequest("Which regions support geo-redundant blob storage?"));

        events.Sources().ShouldBeEmpty();
        events.Done().Kind.ShouldBe(AnswerKind.NotCovered);
        chat.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Model_answers_stream_with_only_valid_citations_reported()
    {
        var chat = new FakeChatClient("Merge the rankings ", "with reciprocal rank fusion [1]. ", "See also [9].");
        var client = await CreateClientWithChatAsync(chat);
        await IngestAsync(client, Notes());

        var events = await AskAsync(client, new AskRequest("How does hybrid search merge rankings?"));

        events.Where(e => e.Type == "delta").Count().ShouldBe(3);
        events.Answer().ShouldBe("Merge the rankings with reciprocal rank fusion [1]. See also [9].");
        events.Done().ShouldBe(new AskDoneEvent(AnswerKind.Ai, [1], null), new DoneComparer());

        var prompt = chat.LastMessages.ShouldNotBeNull();
        prompt[0].Text.ShouldBe(AnswerPrompt.System);
        prompt[^1].Text.ShouldContain("reciprocal rank fusion");
        prompt[^1].Text.ShouldContain("<question>\nHow does hybrid search merge rankings?\n</question>");
    }

    [Fact]
    public async Task An_empty_question_is_rejected()
    {
        var client = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest(" "), Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> CreateClientWithChatAsync(FakeChatClient chat)
    {
        // Shares the database (and the users table) with the main factory; only the AI services differ.
        var withChat = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IChatClient>(chat);
            services.AddSingleton(new AiCapabilities("Fake", "fake-model", "Offline", "offline-hashing-v1", ChatAvailable: true, SemanticSearch: false));
        }));

        var user = await factory.CreateUserClientAsync();
        var client = withChat.CreateClient();
        foreach (var header in user.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    private static async Task<List<RawEvent>> AskAsync(HttpClient client, AskRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/ask", request, Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        var events = new List<RawEvent>();
        foreach (var block in (await response.Content.ReadAsStringAsync(Ct)).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var type = lines.Single(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
            var data = string.Join('\n', lines.Where(l => l.StartsWith("data: ", StringComparison.Ordinal)).Select(l => l["data: ".Length..]));
            events.Add(new RawEvent(type, data));
        }

        return events;
    }

    private static async Task IngestAsync(HttpClient client, byte[] pdf)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "notes.pdf");
        var uploaded = await (await client.PostAsync("/api/documents", form, Ct)).Content.ReadFromJsonAsync<DocumentSummary>(Json, Ct);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var documents = await client.GetFromJsonAsync<List<DocumentSummary>>("/api/documents", Json, Ct);
            if (documents!.Single(d => d.Id == uploaded!.Id).Status == DocumentStatus.Ready)
            {
                return;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException("The document was not processed in time.");
    }

    internal sealed record RawEvent(string Type, string Data);

    private sealed class DoneComparer : IEqualityComparer<AskDoneEvent>
    {
        public bool Equals(AskDoneEvent? x, AskDoneEvent? y) =>
            x!.Kind == y!.Kind && x.Cited.SequenceEqual(y.Cited) && x.Notice == y.Notice;

        public int GetHashCode(AskDoneEvent obj) => obj.Kind.GetHashCode();
    }

    /// <summary>Streams fixed text and records what it was asked.</summary>
    private sealed class FakeChatClient(params string[] chunks) : IChatClient
    {
        public int Calls { get; private set; }
        public List<ChatMessage>? LastMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMessages = messages.ToList();
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }

            yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

internal static class AskEventExtensions
{
    public static List<AskSource> Sources(this List<AskTests.RawEvent> events) =>
        JsonSerializer.Deserialize<SourcesPayload>(events.Single(e => e.Type == "sources").Data, Json)!.Sources;

    public static string Answer(this List<AskTests.RawEvent> events) =>
        string.Concat(events.Where(e => e.Type == "delta").Select(e => JsonSerializer.Deserialize<DeltaPayload>(e.Data, Json)!.Text));

    public static AskDoneEvent Done(this List<AskTests.RawEvent> events)
    {
        var done = JsonSerializer.Deserialize<DonePayload>(events.Single(e => e.Type == "done").Data, Json)!;
        return new AskDoneEvent(done.Kind, done.Cited, done.Notice);
    }

    private sealed record SourcesPayload(List<AskSource> Sources);

    private sealed record DeltaPayload(string Text);

    private sealed record DonePayload(AnswerKind Kind, List<int> Cited, string? Notice);
}
