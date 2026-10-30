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
}
