using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using CertCoach.Application.Ai;
using CertCoach.Application.Ask;
using CertCoach.Application.Billing;
using CertCoach.Application.Documents;
using CertCoach.Domain.Billing;
using CertCoach.Domain.Documents;
using CertCoach.Infrastructure.Persistence;
using CertCoach.Infrastructure.Tests.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Api.IntegrationTests;

/// <summary>
/// The monthly AI allowance, with a fake provider behind the real meter. Each call reports 300 input and 100
/// output tokens: 300 + 5 × 100 = 800 units, against a free allowance of 1,000 and a Pro allowance of 5,000.
/// </summary>
public class BillingTests(CertCoachApiFactory factory)
{
    private const int UnitsPerCall = 800;

    private static readonly object OutlineText = new { text = "Plan (100%)\n- Do it" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_model_call_counts_towards_the_month()
    {
        var client = await ClientAsync(WithAi(new MeteredFakeProvider(), billing: true));

        (await ParseAsync(client)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var allowance = await AllowanceAsync(client);
        allowance.Enforced.ShouldBeTrue();
        allowance.Plan.ShouldBe(Plan.Free);
        allowance.UnitsUsed.ShouldBe(UnitsPerCall);
        allowance.UnitsAllowed.ShouldBe(1000);
        allowance.PercentUsed.ShouldBe(80);
        allowance.ResetsAt.ShouldBe(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Once_the_free_allowance_is_used_model_calls_are_refused_with_402()
    {
        var provider = new MeteredFakeProvider();
        var client = await ClientAsync(WithAi(provider, billing: true));

        // 0 and 800 are under 1,000, so both run; the second ends the month over the limit.
        (await ParseAsync(client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ParseAsync(client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await ParseAsync(client);

        refused.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await refused.Content.ReadAsStringAsync(Ct)).ShouldContain("Upgrade to Pro");
        provider.Calls.ShouldBe(2);
        (await AllowanceAsync(client)).PercentUsed.ShouldBe(100);
    }

    [Fact]
    public async Task Pro_learners_get_the_larger_allowance()
    {
        var client = await ClientAsync(WithAi(new MeteredFakeProvider(), billing: true));
        var until = factory.Clock.GetUtcNow().AddDays(30);
        await GrantProAsync(client, until);

        for (var i = 0; i < 3; i++)
        {
            (await ParseAsync(client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var allowance = await AllowanceAsync(client);
        allowance.Plan.ShouldBe(Plan.Pro);
        allowance.ProUntil.ShouldBe(until);
        allowance.UnitsAllowed.ShouldBe(5000);
        allowance.UnitsUsed.ShouldBe(3 * UnitsPerCall);
    }

    [Fact]
    public async Task Ask_falls_back_to_quotes_when_the_allowance_is_used()
    {
        var provider = new MeteredFakeProvider();
        var client = await ClientAsync(WithAi(provider, billing: true));
        await IngestAsync(client);

        var first = await AskAsync(client);
        first.ShouldContain("\"kind\":\"Ai\"");
        (await AllowanceAsync(client)).UnitsUsed.ShouldBe(UnitsPerCall);

        await AskAsync(client); // 800 is under 1,000, so the model still answers.
        var third = await AskAsync(client);

        third.ShouldContain("\"kind\":\"Quoted\"");
        third.ShouldContain("used this month's AI allowance");
        provider.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task With_billing_off_usage_is_still_counted_but_nothing_is_limited()
    {
        var client = await ClientAsync(WithAi(new MeteredFakeProvider(), billing: false));

        for (var i = 0; i < 3; i++)
        {
            (await ParseAsync(client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var allowance = await AllowanceAsync(client);
        allowance.Enforced.ShouldBeFalse();
        allowance.UnitsUsed.ShouldBe(3 * UnitsPerCall);
    }

    private WebApplicationFactory<Program> WithAi(IChatClient provider, bool billing) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Billing:Enabled", billing ? "true" : "false");
            b.UseSetting("Billing:FreeMonthlyUnits", "1000");
            b.UseSetting("Billing:ProMonthlyUnits", "5000");
            b.ConfigureTestServices(services =>
            {
                // Replaces the provider only, so every call still goes through the real meter.
                services.AddKeyedSingleton(MeteredChatClient.ProviderClientKey, provider);
                services.AddSingleton(new AiCapabilities("Fake", "fake-model", "Offline", "offline-hashing-v2", ChatAvailable: true, SemanticSearch: false));
            });
        });

    private async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> host)
    {
        var user = await factory.CreateUserClientAsync();
        var client = host.CreateClient();
        foreach (var header in user.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    private async Task GrantProAsync(HttpClient client, DateTimeOffset until)
    {
        var userId = Guid.Parse(client.DefaultRequestHeaders.GetValues(UserHeader).Single());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
        var account = BillingAccount.Open(userId, factory.Clock.GetUtcNow());
        account.GrantProUntil(until, factory.Clock.GetUtcNow());
        db.BillingAccounts.Add(account);
        await db.SaveChangesAsync(Ct);
    }

    private static Task<HttpResponseMessage> ParseAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/outlines/parse-ai", OutlineText, Json, Ct);

    private static async Task<AiAllowanceView> AllowanceAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AiAllowanceView>("/api/billing/allowance", Json, Ct)).ShouldNotBeNull();

    private static async Task<string> AskAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest("How does hybrid search merge rankings?"), Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task IngestAsync(HttpClient client)
    {
        var pdf = new TestPdf()
            .Page(("Retrieval notes", TitleSize), ("Hybrid search", HeadingSize),
                ("Hybrid search runs vector search and keyword search. It merges both rankings with reciprocal rank fusion.", BodySize))
            .Build();
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

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("The document was not processed in time.");
    }

    /// <summary>Answers outline parsing with a one-domain outline and Ask with a cited sentence, reporting usage as a real provider does.</summary>
    private sealed class MeteredFakeProvider : IChatClient
    {
        private const string Outline =
            """{"domains":[{"name":"Plan","weightMinPercent":100,"weightMaxPercent":100,"skillAreas":[{"name":"General","objectives":["Do it"]}]}]}""";

        private int _calls;

        public int Calls => _calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Outline)) { FinishReason = ChatFinishReason.Stop, Usage = Usage() });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Reciprocal rank fusion merges the rankings [1].");
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(Usage())]) { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static UsageDetails Usage() => new() { InputTokenCount = 300, OutputTokenCount = 100, TotalTokenCount = 400 };
    }
}
