using System.Net;
using System.Net.Http.Json;
using System.Text;
using CertCoach.Application.Certifications.Outlines.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

/// <summary>Outline import over HTTP, offline, with the web served by a stub so no test reaches the internet.</summary>
public class OutlineImportTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string ExamPage = """
        <html><head><title>Exam DEMO-101 | Contoso Learn</title></head><body><main>
          <h1>Exam DEMO-101: Contoso AI Solutions Engineer</h1>
          <h2>Skills measured</h2>
          <ul><li>Plan and manage an AI solution (40–45%)</li><li>Build generative AI applications (55–60%)</li></ul>
          <a href="/credentials/study-guides/demo-101">Review the study guide</a>
        </main></body></html>
        """;

    private const string StudyGuide = """
        <html><body><main>
          <h1>Study guide for Exam DEMO-101: Contoso AI Solutions Engineer</h1>
          <p>Skills measured as of April 18, 2026.</p>
          <h2>Plan and manage an AI solution (40–45%)</h2>
          <h3>Select the right AI services</h3><ul><li>Choose a model for a text generation scenario</li></ul>
          <h2>Build generative AI applications (55–60%)</h2>
          <h3>Build chat apps</h3><ul><li>Ground answers in your data</li></ul>
        </main></body></html>
        """;

    [Fact]
    public async Task Imports_an_outline_by_following_the_study_guide_link()
    {
        var client = await ClientAsync(new StubWeb(new()
        {
            ["https://learn.contoso.com/credentials/exams/demo-101"] = ExamPage,
            ["https://learn.contoso.com/credentials/study-guides/demo-101"] = StudyGuide,
        }));

        var response = await client.PostAsJsonAsync("/api/outlines/import-url", new ImportOutlineRequest("https://learn.contoso.com/credentials/exams/demo-101"), Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var import = (await response.Content.ReadFromJsonAsync<OutlineImport>(Json, Ct)).ShouldNotBeNull();
        import.Method.ShouldBe(OutlineImportMethod.Parser);
        import.ExamCode.ShouldBe("DEMO-101");
        import.OutlineVersionDate.ShouldBe(new DateOnly(2026, 4, 18));
        import.PagesRead.Count.ShouldBe(2);
        import.Draft.ObjectiveCount.ShouldBe(2);
    }

    [Fact]
    public async Task Private_addresses_are_refused()
    {
        var client = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/outlines/import-url", new ImportOutlineRequest("http://169.254.169.254/latest/meta-data/"), Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Detail.ShouldNotBeNull().ShouldContain("private network");
    }

    private async Task<HttpClient> ClientAsync(StubWeb web)
    {
        var withWeb = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            // The fetcher's typed client is named after its interface; this replaces the real network handler.
            services.AddHttpClient(nameof(IWebPageFetcher)).ConfigurePrimaryHttpMessageHandler(() => web)));

        var user = await factory.CreateUserClientAsync();
        var client = withWeb.CreateClient();
        foreach (var header in user.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    private sealed class StubWeb(Dictionary<string, string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var html)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html"), RequestMessage = request }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found", RequestMessage = request });
    }
}
