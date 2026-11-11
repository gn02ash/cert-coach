using System.Net;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Resources;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Resources;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class ResourceTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LearnModule RagModule = new(
        "learn.rag", "Implement retrieval augmented generation with vector search", "Ground answers in your own data.",
        new Uri("https://learn.microsoft.com/en-us/training/modules/rag/"), 45, ["intermediate"]);

    [Fact]
    public async Task Suggestions_come_from_the_catalogue_and_video_search_and_can_be_saved_ticked_and_deleted()
    {
        var withProviders = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILearnCatalog>(new FakeCatalog(new LearnIndex([RagModule])));
            services.AddSingleton<IVideoSearch>(new FakeVideos());
        }));
        var (client, certification) = await SetUpAsync(withProviders.CreateClient());
        var objective = certification.Domains[0].SkillAreas[0].Objectives[0];

        var suggestions = await GetAsync<ResourceSuggestions>(client, $"/api/resources/suggestions?objectiveId={objective.Id}");
        suggestions.Learn.ShouldHaveSingleItem().Title.ShouldBe(RagModule.Title);
        suggestions.Videos.ShouldHaveSingleItem().Source.ShouldBe("Contoso Channel");
        suggestions.YouTubeAvailable.ShouldBeTrue();
        suggestions.YouTubeSearchUrl.Query.ShouldContain("Combine%20vector%20search");

        var module = suggestions.Learn[0];
        var request = new SaveResourceRequest(certification.Id, objective.Id, module.Kind, module.Title, module.Url.AbsoluteUri, module.Source, module.Summary, module.DurationMinutes);
        var saved = await SendAsync<ResourceView>(client, HttpMethod.Post, "/api/resources", request);
        (await SendAsync<ResourceView>(client, HttpMethod.Post, "/api/resources", request)).Id.ShouldBe(saved.Id); // Saved once.
        (await GetAsync<ResourceSuggestions>(client, $"/api/resources/suggestions?objectiveId={objective.Id}")).Learn[0].Saved.ShouldBeTrue();

        (await SendAsync<ResourceView>(client, HttpMethod.Put, $"/api/resources/{saved.Id}/done", new SetResourceDoneRequest(true))).Done.ShouldBeTrue();
        (await GetAsync<List<ResourceView>>(client, $"/api/resources?certificationId={certification.Id}")).ShouldHaveSingleItem().DurationMinutes.ShouldBe(45);

        (await client.DeleteAsync($"/api/resources/{saved.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<List<ResourceView>>(client, $"/api/resources?certificationId={certification.Id}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_catalogue_or_a_key_there_is_still_a_search_link_and_links_can_be_added_by_hand()
    {
        var (client, certification) = await SetUpAsync(factory.CreateClient());
        var objective = certification.Domains[0].SkillAreas[0].Objectives[0];

        var suggestions = await GetAsync<ResourceSuggestions>(client, $"/api/resources/suggestions?objectiveId={objective.Id}");
        suggestions.Learn.ShouldBeEmpty();
        suggestions.Videos.ShouldBeEmpty();
        suggestions.YouTubeAvailable.ShouldBeFalse();
        suggestions.YouTubeSearchUrl.Host.ShouldBe("www.youtube.com");
        suggestions.Notes.ShouldHaveSingleItem().ShouldContain("Microsoft Learn");

        var link = await SendAsync<ResourceView>(client, HttpMethod.Post, "/api/resources",
            new SaveResourceRequest(certification.Id, null, ResourceKind.Link, " My notes ", "https://example.com/notes", null, null, null));
        link.Title.ShouldBe("My notes");
        link.Source.ShouldBe("example.com");

        async Task<HttpStatusCode> SaveAsync(SaveResourceRequest request) => (await client.PostAsJsonAsync("/api/resources", request, Json, Ct)).StatusCode;
        (await SaveAsync(new(certification.Id, null, ResourceKind.Link, "Script", "javascript:alert(1)", null, null, null))).ShouldBe(HttpStatusCode.BadRequest);
        (await SaveAsync(new(certification.Id, null, ResourceKind.LearnModule, "Not Learn", "https://evil.example/learn.microsoft.com", null, null, null))).ShouldBe(HttpStatusCode.BadRequest);
        (await SaveAsync(new(certification.Id, Guid.NewGuid(), ResourceKind.Link, "Other objective", "https://example.com", null, null, null))).ShouldBe(HttpStatusCode.BadRequest);

        var other = await factory.CreateUserClientAsync();
        (await other.GetAsync($"/api/resources?certificationId={certification.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/resources/suggestions?objectiveId={objective.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/resources/{link.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<(HttpClient, CertificationDetail)> SetUpAsync(HttpClient client)
    {
        var user = await factory.CreateUserClientAsync();
        foreach (var header in user.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", new SaveCertificationRequest(
            "RES-" + Random.Shared.Next(100, 999), "Resources", "Contoso", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Retrieval", 100, 100, [new SkillAreaDraft("Search", ["Combine vector search and keyword search"])])]));
        return (client, certification);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
        where T : class =>
        (await client.GetFromJsonAsync<T>(url, Json, Ct)).ShouldNotBeNull();

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object body)
        where T : class
    {
        var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) }, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct)).ShouldNotBeNull();
    }

    private sealed class FakeCatalog(LearnIndex index) : ILearnCatalog
    {
        public Task<LearnIndex?> GetAsync(CancellationToken ct) => Task.FromResult<LearnIndex?>(index);
    }

    private sealed class FakeVideos : IVideoSearch
    {
        public bool Available => true;

        public Task<IReadOnlyList<VideoResult>?> SearchAsync(string query, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VideoResult>?>([new VideoResult($"Video about {query}", new Uri("https://www.youtube.com/watch?v=abc123"), "Contoso Channel", null)]);
    }
}
