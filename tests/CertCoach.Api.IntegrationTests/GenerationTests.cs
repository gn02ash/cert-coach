using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Documents;
using CertCoach.Application.Practice;
using CertCoach.Application.Practice.Generation;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Documents;
using CertCoach.Domain.Practice;
using CertCoach.Infrastructure.Tests.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Api.IntegrationTests;

/// <summary>
/// Question generation. Kept in one class, so its tests run one at a time: only one of them runs a worker, and no
/// other host's worker can pick up a job meant for a host with different AI settings.
/// </summary>
public class GenerationTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Notes() => new TestPdf()
        .Page(("Retrieval notes", TitleSize), ("Hybrid search", HeadingSize),
            ("Hybrid search runs vector search and keyword search in parallel. Reciprocal rank fusion merges both rankings into one list. " +
             "HNSW indexes make approximate nearest neighbour search fast on large collections. BM25 weighs rare terms more heavily than common ones.", BodySize))
        .Build();

    private static SaveCertificationRequest Certification() => new(
        "GEN-" + Random.Shared.Next(100, 999), "Generation", "Contoso", null, null, CertificationStatus.Active, null,
        [new DomainDraft("Retrieval", 60, 60, [new SkillAreaDraft("Search", ["Combine vector and keyword search"])]),
         new DomainDraft("Operations", 40, 40, [new SkillAreaDraft("Billing", ["Negotiate enterprise licensing discounts"])])]);

    [Fact]
    public async Task The_worker_writes_cited_fill_in_the_blank_items_offline()
    {
        await using var withWorker = factory.WithWebHostBuilder(b => b.UseSetting("Generation:Enabled", "true"));
        var client = await UserClientAsync(withWorker);
        var certification = await PostAsync<CertificationDetail>(client, "/api/certifications", Certification());
        await IngestAsync(client, Notes());

        var run = await StartAsync(client, new GenerateItemsRequest(certification.Id, PerObjective: 3));
        run.UsesAi.ShouldBeFalse();
        var finished = await WaitAsync(client, run.Id);

        finished.Status.ShouldBe(GenerationStatus.Completed);
        finished.ObjectivesDone.ShouldBe(2);
        finished.ObjectivesWithoutMaterial.ShouldBe(1); // Nothing in the notes about licensing.
        finished.ItemsCreated.ShouldBe(3);

        var items = await ItemsAsync(client, certification.Id);
        items.Count.ShouldBe(3);
        items.ShouldAllBe(i => i.Origin == PracticeItemOrigin.Documents && i.ObjectiveText == "Combine vector and keyword search");
        items.ShouldAllBe(i => i.Sources.Count == 1 && i.Sources[0].Page == 1 && i.Sources[0].DocumentTitle != null);
        items.ShouldContain(i => i.Kind == PracticeItemKind.Flashcard);

        // A second run adds only items the objective doesn't have yet.
        var again = await WaitAsync(client, (await StartAsync(client, new GenerateItemsRequest(certification.Id, PerObjective: 3))).Id);
        (await ItemsAsync(client, certification.Id)).Select(i => i.Prompt).Distinct().Count().ShouldBe(3 + again.ItemsCreated);
    }

    [Fact]
    public async Task Model_items_are_validated_and_labelled_by_where_they_came_from()
    {
        var chat = new ScriptedChatClient(prompt => prompt.Contains("<sources>", StringComparison.Ordinal)
            ? """
              {"items":[
                {"kind":"SingleChoice","prompt":"You need one ranked list from vector and keyword results. What should you use?",
                 "options":["Reciprocal rank fusion","HNSW","BM25","Cosine distance"],"correctOptions":[0],
                 "answer":"RRF merges rankings.","sources":[1],"quote":"Reciprocal rank fusion merges both rankings into one list."},
                {"kind":"MultipleChoice","prompt":"Pick two. (Choose two.)","options":["A","B","C","D"],"correctOptions":[0],
                 "answer":"Invalid: one correct option.","sources":[1],"quote":null},
                {"kind":"Flashcard","prompt":"What does HNSW speed up?","options":[],"correctOptions":[],
                 "answer":"Approximate nearest neighbour search.","sources":[7],"quote":null}]}
              """
            : """
              {"items":[{"kind":"Flashcard","prompt":"What is an enterprise agreement?","options":[],"correctOptions":[],
                "answer":"A volume licensing contract.","sources":[],"quote":null}]}
              """);
        var withChat = WithChat(chat);
        var client = await UserClientAsync(withChat);
        var certification = await PostAsync<CertificationDetail>(client, "/api/certifications", Certification());
        await IngestAsync(client, Notes());

        var run = await StartAsync(client, new GenerateItemsRequest(certification.Id, PerObjective: 3));
        run.UsesAi.ShouldBeTrue();
        await RunAsync(withChat, client, run.Id);

        var finished = await GetAsync<GenerationJobView>(client, $"/api/practice/generations/{run.Id}");
        (finished.Status, finished.ItemsCreated, finished.ItemsRejected, finished.ObjectivesWithoutMaterial)
            .ShouldBe((GenerationStatus.Completed, 2, 2, 1));

        var items = await ItemsAsync(client, certification.Id);
        var cited = items.Single(i => i.Origin == PracticeItemOrigin.Documents);
        cited.Sources.ShouldHaveSingleItem().Quote.ShouldBe("Reciprocal rank fusion merges both rankings into one list.");
        var knowledge = items.Single(i => i.Origin == PracticeItemOrigin.ModelKnowledge);
        knowledge.ObjectiveText.ShouldBe("Negotiate enterprise licensing discounts");
        knowledge.Sources.ShouldBeEmpty();
        chat.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task One_run_at_a_time_per_certification_and_a_cancelled_run_stops()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await PostAsync<CertificationDetail>(client, "/api/certifications", Certification());

        var run = await StartAsync(client, new GenerateItemsRequest(certification.Id, DomainId: certification.Domains[0].Id));
        var second = await client.PostAsJsonAsync("/api/practice/generations", new GenerateItemsRequest(certification.Id), Json, Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        run.Objectives.ShouldBe(1);

        var cancelled = await PostAsync<GenerationJobView>(client, $"/api/practice/generations/{run.Id}/cancel");
        cancelled.Status.ShouldBe(GenerationStatus.Cancelled);
        await RunAsync(factory, client, run.Id);
        (await GetAsync<GenerationJobView>(client, $"/api/practice/generations/{run.Id}")).ObjectivesDone.ShouldBe(0);

        (await client.GetFromJsonAsync<List<GenerationJobView>>($"/api/practice/generations?certificationId={certification.Id}", Json, Ct))
            .ShouldNotBeNull().ShouldHaveSingleItem().Id.ShouldBe(run.Id);
        (await client.PostAsJsonAsync("/api/practice/generations", new GenerateItemsRequest(certification.Id, PerObjective: 11), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private WebApplicationFactory<Program> WithChat(IChatClient chat) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton(chat);
            services.AddSingleton(new AiCapabilities("Fake", "fake-model", "Offline", "offline-hashing-v2", ChatAvailable: true, SemanticSearch: false));
        }));

    /// <summary>What the worker does with a claimed run, without a worker that other tests' hosts could race.</summary>
    private static async Task RunAsync(WebApplicationFactory<Program> host, HttpClient client, Guid runId)
    {
        var me = await client.GetFromJsonAsync<CertCoach.Application.Users.UserProfile>("/auth/me", Json, Ct);
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundUser>().Set(me!.Id, "UTC");
        await scope.ServiceProvider.GetRequiredService<GenerationService>().RunAsync(runId, Ct);
    }

    private async Task<HttpClient> UserClientAsync(WebApplicationFactory<Program> host)
    {
        var user = await factory.CreateUserClientAsync();
        var client = host.CreateClient();
        foreach (var header in user.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    private static async Task<GenerationJobView> StartAsync(HttpClient client, GenerateItemsRequest request) =>
        await PostAsync<GenerationJobView>(client, "/api/practice/generations", request);

    private static async Task<GenerationJobView> WaitAsync(HttpClient client, Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = await GetAsync<GenerationJobView>(client, $"/api/practice/generations/{runId}");
            if (run.Status is not (GenerationStatus.Queued or GenerationStatus.Running))
            {
                return run;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException("The generation run did not finish in time.");
    }

    private static async Task<List<PracticeItemView>> ItemsAsync(HttpClient client, Guid certificationId) =>
        await GetAsync<List<PracticeItemView>>(client, $"/api/practice/items?certificationId={certificationId}");

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
        where T : class =>
        (await client.GetFromJsonAsync<T>(url, Json, Ct)).ShouldNotBeNull();

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
        where T : class
    {
        var response = await client.PostAsJsonAsync(url, body, Json, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct)).ShouldNotBeNull();
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

    private sealed class ScriptedChatClient(Func<string, string> answer) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var json = answer(messages.Last().Text);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)) { FinishReason = ChatFinishReason.Stop });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
