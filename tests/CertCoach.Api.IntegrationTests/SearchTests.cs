using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Documents;
using CertCoach.Application.Search;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Documents;
using CertCoach.Infrastructure.Tests.Documents;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Api.IntegrationTests;

/// <summary>Hybrid search through the API over documents ingested by the real worker, with offline embeddings.</summary>
public class SearchTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Notes(string title, params (string Heading, string Body)[] sections)
    {
        var pdf = new TestPdf();
        pdf.Page([(title, TitleSize), .. sections.Take(1).SelectMany(s => new[] { (s.Heading, HeadingSize), (s.Body, BodySize) })]);
        foreach (var (heading, body) in sections.Skip(1))
        {
            pdf.Page((heading, HeadingSize), (body, BodySize));
        }

        return pdf.Build();
    }

    private static readonly byte[] RetrievalNotes = Notes(
        "Retrieval notes",
        ("Chunking", "Split documents into chunks of a few hundred tokens and keep the page number of every chunk for citations."),
        ("Hybrid search", "Hybrid search runs vector search and keyword search, then merges both rankings with reciprocal rank fusion."),
        ("Prompt caching", "Prompt caching reuses a long system prompt across requests, which lowers latency and cost."));

    private static readonly byte[] SecurityNotes = Notes(
        "Security notes",
        ("Managed identity", "A managed identity lets a service call Azure resources without storing any secret."),
        ("Key Vault", "Key Vault stores secrets, keys and certificates; grant access with role assignments."));

    [Fact]
    public async Task Finds_the_passage_that_answers_a_question_and_cites_its_page()
    {
        var client = await factory.CreateUserClientAsync();
        await IngestAsync(client, RetrievalNotes, "retrieval.pdf");
        await IngestAsync(client, SecurityNotes, "security.pdf");

        var result = await SearchAsync(client, "How do I merge vector and keyword search rankings?");

        var top = result.Hits[0];
        top.DocumentTitle.ShouldBe("Retrieval notes");
        top.HeadingPath.ShouldBe("Retrieval notes › Hybrid search");
        top.PageStart.ShouldBe(2);
        top.VectorRank.ShouldBe(1);
        top.TextRank.ShouldBe(1);
        top.Similarity.ShouldNotBeNull().ShouldBeGreaterThan(0);
        result.Semantic.ShouldBeFalse();
    }

    [Fact]
    public async Task Full_text_search_matches_any_word_of_a_long_question()
    {
        var client = await factory.CreateUserClientAsync();
        await IngestAsync(client, SecurityNotes, "security.pdf");

        var result = await SearchAsync(client, "Where should my application keep its database certificates safely?", SearchMode.Text);

        result.Hits[0].HeadingPath.ShouldBe("Security notes › Key Vault");
        result.Hits.ShouldAllBe(h => h.VectorRank == null && h.TextRank != null);
    }

    [Fact]
    public async Task A_question_of_stop_words_only_finds_nothing_by_text()
    {
        var client = await factory.CreateUserClientAsync();
        await IngestAsync(client, SecurityNotes, "security.pdf");

        (await SearchAsync(client, "what is the", SearchMode.Text)).Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Searches_only_the_users_own_documents()
    {
        var owner = await factory.CreateUserClientAsync();
        var other = await factory.CreateUserClientAsync();
        await IngestAsync(owner, SecurityNotes, "security.pdf");

        (await SearchAsync(other, "managed identity secrets")).Hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_certification_scope_keeps_its_documents_and_untagged_ones()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await CreateCertificationAsync(client, "SCOPE-1");
        var otherCertification = await CreateCertificationAsync(client, "SCOPE-2");
        await IngestAsync(client, SecurityNotes, "security.pdf", certification);
        await IngestAsync(client, RetrievalNotes, "retrieval.pdf", otherCertification);
        await IngestAsync(client, Notes("General notes", ("Secrets", "Never commit secrets to a repository.")), "general.pdf");

        var result = await SearchAsync(client, "secrets", certificationId: certification);

        result.Hits.Select(h => h.DocumentTitle).Distinct().Order().ShouldBe(["General notes", "Security notes"]);
    }

    [Fact]
    public async Task An_empty_query_is_rejected()
    {
        var client = await factory.CreateUserClientAsync();

        var response = await client.GetAsync("/api/search?q=%20", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<SearchResponse> SearchAsync(
        HttpClient client, string query, SearchMode mode = SearchMode.Hybrid, Guid? certificationId = null)
    {
        var url = $"/api/search?q={Uri.EscapeDataString(query)}&mode={mode}";
        if (certificationId is { } id)
        {
            url += $"&certificationId={id}";
        }

        return (await client.GetFromJsonAsync<SearchResponse>(url, Json, Ct)).ShouldNotBeNull();
    }

    private static async Task<Guid> CreateCertificationAsync(HttpClient client, string examCode)
    {
        var request = new SaveCertificationRequest(
            examCode, examCode, "Demo", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Everything", 100, 100, [new SkillAreaDraft("All", ["Learn it"])])]);
        var response = await client.PostAsJsonAsync("/api/certifications", request, Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<IdOnly>(Json, Ct))!.Id;
    }

    private sealed record IdOnly(Guid Id);

    private static async Task IngestAsync(HttpClient client, byte[] pdf, string fileName, Guid? certificationId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        if (certificationId is { } id)
        {
            form.Add(new StringContent(id.ToString()), "certificationId");
        }

        var response = await client.PostAsync("/api/documents", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var uploaded = (await response.Content.ReadFromJsonAsync<DocumentSummary>(Json, Ct))!;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var documents = await client.GetFromJsonAsync<List<DocumentSummary>>("/api/documents", Json, Ct);
            var status = documents!.Single(d => d.Id == uploaded.Id).Status;
            if (status == DocumentStatus.Ready)
            {
                return;
            }

            status.ShouldNotBe(DocumentStatus.Failed);
            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"{fileName} was not processed in time.");
    }
}
