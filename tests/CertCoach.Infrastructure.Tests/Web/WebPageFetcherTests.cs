using System.Net;
using System.Text;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Certifications.Outlines.Import;
using CertCoach.Infrastructure.Documents;
using CertCoach.Infrastructure.Tests.Documents;
using CertCoach.Infrastructure.Web;

namespace CertCoach.Infrastructure.Tests.Web;

public class HtmlTextTests
{
    /// <summary>Synthetic, in the layout of a Microsoft Learn study guide.</summary>
    private const string StudyGuide = """
        <!doctype html>
        <html><head><title>Study guide for Exam DEMO-101 | Contoso Learn</title><base href="https://learn.contoso.com/credentials/"></head>
        <body>
          <header><nav><a href="/">Home</a><a href="/training">Training</a></nav></header>
          <main>
            <h1>Study guide for Exam DEMO-101: Contoso AI Solutions Engineer</h1>
            <p>Skills measured as of <strong>April 18, 2026</strong>.</p>
            <script>var tracking = "Plan (50%)";</script>
            <h2>Skills at a glance</h2>
            <ul><li>Plan and manage an AI solution (40–45%)</li><li>Build generative AI applications (55–60%)</li></ul>
            <h2>Plan and manage an AI solution (40–45%)</h2>
            <h3>Select the right
              AI services</h3>
            <ul>
              <li><p>Choose a model for a <em>text generation</em> scenario</p></li>
              <li>Estimate cost<ul><li>Token usage</li></ul></li>
            </ul>
            <h2>Build generative AI applications (55–60%)</h2>
            <h3>Build chat apps</h3>
            <ul><li>Ground answers in your data</li></ul>
            <h2>Change log</h2>
            <table><tr><th>Before</th><th>After</th></tr><tr><td>Old skill</td><td>New skill</td></tr></table>
            <div hidden>Hidden (10%)</div>
            <a href="study-guides/demo-102#top">Next study guide</a>
            <a href="mailto:someone@contoso.com">Mail</a>
          </main>
          <footer>Privacy (100%)</footer>
        </body></html>
        """;

    [Fact]
    public void Writes_the_main_content_as_markdown_the_parser_reads()
    {
        var page = HtmlText.Convert(StudyGuide, new Uri("https://learn.contoso.com/credentials/study-guides/demo-101"));

        page.Title.ShouldBe("Study guide for Exam DEMO-101: Contoso AI Solutions Engineer");
        page.Text.ShouldBe("""
            # Study guide for Exam DEMO-101: Contoso AI Solutions Engineer
            Skills measured as of April 18, 2026.
            ## Skills at a glance
            - Plan and manage an AI solution (40–45%)
            - Build generative AI applications (55–60%)
            ## Plan and manage an AI solution (40–45%)
            ### Select the right AI services
            - Choose a model for a text generation scenario
            - Estimate cost
            - Token usage
            ## Build generative AI applications (55–60%)
            ### Build chat apps
            - Ground answers in your data
            ## Change log
            Before | After
            Old skill | New skill
            Next study guide Mail
            """.ReplaceLineEndings("\n"));

        var draft = OutlineParser.Parse(page.Text);
        draft.Domains.Select(d => d.Name).ShouldBe(["Plan and manage an AI solution", "Build generative AI applications"]);
        draft.ObjectiveCount.ShouldBe(4);
    }

    [Fact]
    public void Resolves_links_against_the_base_and_drops_fragments_and_other_schemes()
    {
        var page = HtmlText.Convert(StudyGuide, new Uri("https://learn.contoso.com/credentials/study-guides/demo-101"));

        page.Links.Select(l => (l.Url.AbsoluteUri, l.Text)).ShouldBe(
        [
            ("https://learn.contoso.com/", "Home"),
            ("https://learn.contoso.com/training", "Training"),
            ("https://learn.contoso.com/credentials/study-guides/demo-102", "Next study guide"),
        ]);
    }
}

public class PublicNetworkTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:c0a8:0101::1")]
    public void Blocks_private_and_special_addresses(string address)
    {
        PublicNetwork.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("13.107.246.40")]
    [InlineData("172.32.0.1")]
    [InlineData("2603:1030::1")]
    [InlineData("64:ff9b::d6b:f628")]
    public void Allows_public_addresses(string address)
    {
        PublicNetwork.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://localhost:9/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task Never_connects_to_a_private_address(string url)
    {
        using var http = new HttpClient(PublicNetwork.CreateHandler());
        var fetcher = new HttpWebPageFetcher(http, new PdfPigTextExtractor());

        var error = await Should.ThrowAsync<PageFetchException>(() => fetcher.FetchAsync(new Uri(url), TestContext.Current.CancellationToken));

        error.Message.ShouldContain("private network");
    }
}

public class HttpWebPageFetcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_html_and_reports_where_redirects_ended()
    {
        var fetcher = Fetcher(_ => Html("<main><h1>Guide</h1><ul><li>Objective</li></ul></main>"), finalUrl: new Uri("https://learn.contoso.com/en-us/guide"));

        var page = await fetcher.FetchAsync(new Uri("https://learn.contoso.com/guide"), Ct);

        page.Url.ShouldBe(new Uri("https://learn.contoso.com/en-us/guide"));
        page.Text.ShouldBe("# Guide\n- Objective");
    }

    [Fact]
    public async Task Reads_pdfs_with_the_upload_extractor()
    {
        var pdf = new TestPdf()
            .Page(("DEMO-101 exam guide", TestPdf.TitleSize), ("Plan the solution (40%)", TestPdf.HeadingSize), ("Choose a model for the workload.", TestPdf.BodySize))
            .Build();
        var fetcher = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pdf) { Headers = { ContentType = new("application/pdf") } } });

        var page = await fetcher.FetchAsync(new Uri("https://contoso.com/guide.pdf"), Ct);

        page.Text.ShouldContain("# Plan the solution (40%)");
        page.Text.ShouldContain("Choose a model for the workload.");
    }

    [Fact]
    public async Task Rejects_errors_other_file_types_and_oversized_pages()
    {
        var notFound = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
        var image = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/png") } } });
        var huge = Fetcher(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[HttpWebPageFetcher.MaxBytes + 1])) });
        var url = new Uri("https://contoso.com/page");

        (await Should.ThrowAsync<PageFetchException>(() => notFound.FetchAsync(url, Ct))).Message.ShouldBe("https://contoso.com/page returned 404 Not Found.");
        (await Should.ThrowAsync<PageFetchException>(() => image.FetchAsync(url, Ct))).Message.ShouldContain("image/png");
        (await Should.ThrowAsync<PageFetchException>(() => huge.FetchAsync(url, Ct))).Message.ShouldContain("larger than 5 MB");
        await Should.ThrowAsync<PageFetchException>(() => notFound.FetchAsync(new Uri("file:///etc/passwd"), Ct));
    }

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpWebPageFetcher Fetcher(Func<HttpRequestMessage, HttpResponseMessage> respond, Uri? finalUrl = null) =>
        new(new HttpClient(new StubHandler(respond, finalUrl)), new PdfPigTextExtractor());

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, Uri? finalUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (finalUrl is not null)
            {
                request.RequestUri = finalUrl; // What SocketsHttpHandler does after following redirects.
            }

            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
