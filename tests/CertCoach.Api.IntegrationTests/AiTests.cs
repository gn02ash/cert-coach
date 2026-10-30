using System.Net.Http.Json;
using CertCoach.Application.Ai;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class AiTests(CertCoachApiFactory factory)
{
    [Fact]
    public async Task Runs_offline_without_any_keys()
    {
        var client = await factory.CreateUserClientAsync();

        var status = await client.GetFromJsonAsync<AiCapabilities>("/api/ai/status", Json, TestContext.Current.CancellationToken);

        status.ShouldNotBeNull();
        status.ChatProvider.ShouldBe("Offline");
        status.ChatAvailable.ShouldBeFalse();
        status.SemanticSearch.ShouldBeFalse();
    }

    [Fact]
    public async Task Ai_outline_parsing_explains_that_it_needs_a_chat_provider()
    {
        var client = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/outlines/parse-ai", new { text = "Plan (100%)\n- Do it" }, Json, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("needs a chat provider");
    }
}
