using System.Net;
using System.Text;
using CertCoach.Infrastructure.Resources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CertCoach.Infrastructure.Tests.Resources;

public class ResourceProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_learn_catalogue_is_downloaded_once_cleaned_and_kept_for_a_day()
    {
        var web = new StubWeb(_ => Json("""
            {"modules":[
              {"uid":"learn.rag","title":"Implement RAG with vector search","summary":"Ground answers.","duration_in_minutes":45,"levels":["intermediate"],
               "url":"https://learn.microsoft.com/en-us/training/modules/rag/?WT.mc_id=api_CatalogApi"},
              {"uid":"elsewhere","title":"Not on Learn","summary":"","url":"https://example.com/module"},
              {"uid":"untitled","title":"","url":"https://learn.microsoft.com/x"}]}
            """));
        var clock = new FakeTimeProvider();
        using var catalog = new MicrosoftLearnCatalog(web, clock, NullLogger<MicrosoftLearnCatalog>.Instance);

        var index = (await catalog.GetAsync(Ct)).ShouldNotBeNull();
        await catalog.GetAsync(Ct);
        clock.Advance(TimeSpan.FromHours(25));
        await catalog.GetAsync(Ct);

        index.Count.ShouldBe(1);
        var match = index.Search("Implement RAG with vector search", 5).ShouldHaveSingleItem();
        match.Module.Url.AbsoluteUri.ShouldBe("https://learn.microsoft.com/en-us/training/modules/rag/");
        match.Module.DurationMinutes.ShouldBe(45);
        web.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task A_failed_download_gives_no_catalogue_and_is_retried_later()
    {
        var web = new StubWeb(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var clock = new FakeTimeProvider();
        using var catalog = new MicrosoftLearnCatalog(web, clock, NullLogger<MicrosoftLearnCatalog>.Instance);

        (await catalog.GetAsync(Ct)).ShouldBeNull();
        (await catalog.GetAsync(Ct)).ShouldBeNull();
        web.Requests.ShouldBe(1);

        clock.Advance(TimeSpan.FromMinutes(6));
        await catalog.GetAsync(Ct);
        web.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task Youtube_search_sends_the_key_in_a_header_decodes_titles_and_remembers_results()
    {
        var web = new StubWeb(_ => Json("""
            {"items":[
              {"id":{"videoId":"abc123"},"snippet":{"title":"RAG &amp; vector search","channelTitle":"Contoso","description":"A walkthrough."}},
              {"id":{},"snippet":{"title":"A channel, not a video"}}]}
            """));
        var search = new YouTubeVideoSearch(web, "secret-key", new FakeTimeProvider(), NullLogger<YouTubeVideoSearch>.Instance);

        var videos = (await search.SearchAsync("AI-103 vector search", 5, Ct)).ShouldNotBeNull();
        await search.SearchAsync("AI-103 vector search", 5, Ct);

        var video = videos.ShouldHaveSingleItem();
        video.Title.ShouldBe("RAG & vector search");
        video.Url.AbsoluteUri.ShouldBe("https://www.youtube.com/watch?v=abc123");
        web.Requests.ShouldBe(1);
        web.Last!.RequestUri!.Query.ShouldNotContain("secret-key");
        web.Last.RequestUri.Query.ShouldContain("q=AI-103%20vector%20search");
        web.Last.Headers.GetValues("X-Goog-Api-Key").ShouldBe(["secret-key"]);
    }

    [Fact]
    public async Task Youtube_search_is_off_without_a_key_and_reports_failures_as_no_result()
    {
        var web = new StubWeb(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var off = new YouTubeVideoSearch(web, " ", new FakeTimeProvider(), NullLogger<YouTubeVideoSearch>.Instance);
        var overQuota = new YouTubeVideoSearch(web, "key", new FakeTimeProvider(), NullLogger<YouTubeVideoSearch>.Instance);

        off.Available.ShouldBeFalse();
        (await off.SearchAsync("x", 5, Ct)).ShouldBeNull();
        (await overQuota.SearchAsync("x", 5, Ct)).ShouldBeNull();
        web.Requests.ShouldBe(1);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubWeb(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests { get; private set; }
        public HttpRequestMessage? Last { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Last = request;
            return Task.FromResult(respond(request));
        }
    }
}
