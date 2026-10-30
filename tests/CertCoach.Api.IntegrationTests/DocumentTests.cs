using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CertCoach.Application.Documents;
using CertCoach.Domain.Documents;
using CertCoach.Infrastructure.Persistence;
using CertCoach.Infrastructure.Tests.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Api.IntegrationTests;

/// <summary>Upload to searchable chunks through the real background worker, with offline embeddings.</summary>
public class DocumentTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] StudyNotes(string topic = "Hybrid search") => new TestPdf()
        .Page(("Study notes", TitleSize), ("Retrieval", HeadingSize),
            ("Retrieval-augmented generation grounds answers in your own documents. " +
             "The application retrieves related passages first.", BodySize))
        .Page((topic, HeadingSize),
            ($"{topic} combines a vector search with a keyword search and merges the rankings.", BodySize))
        .Build();

    [Fact]
    public async Task An_uploaded_pdf_is_processed_into_cited_chunks()
    {
        var client = await factory.CreateUserClientAsync();
        var pdf = StudyNotes();

        var uploaded = await UploadAsync(client, pdf, "study_notes.pdf");
        uploaded.Status.ShouldBe(DocumentStatus.Pending);
        uploaded.Title.ShouldBe("study notes");

        var ready = await WaitUntilDoneAsync(client, uploaded.Id);
        ready.Status.ShouldBe(DocumentStatus.Ready, ready.Error);
        ready.Title.ShouldBe("Study notes");
        ready.PageCount.ShouldBe(2);
        ready.ChunkCount.ShouldBeGreaterThan(0);

        var chunks = await ChunksAsync(uploaded.Id);
        chunks.Count.ShouldBe(ready.ChunkCount);
        chunks.ShouldContain(c => c.HeadingPath == "Study notes › Hybrid search" && c.PageStart == 2);
        chunks.ShouldAllBe(c => c.Embedding.Any(v => v != 0));

        var file = await client.GetAsync($"/api/documents/{uploaded.Id}/file", Ct);
        file.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        file.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        (await file.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(pdf);
    }

    [Fact]
    public async Task The_same_pdf_cannot_be_uploaded_twice()
    {
        var client = await factory.CreateUserClientAsync();
        var pdf = StudyNotes();
        await UploadAsync(client, pdf, "a.pdf");

        var response = await PostFileAsync(client, pdf, "b.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_pdfs_are_accepted()
    {
        var client = await factory.CreateUserClientAsync();

        var response = await PostFileAsync(client, "just text"u8.ToArray(), "notes.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_pdf_without_text_fails_with_a_reason_and_can_be_retried()
    {
        var client = await factory.CreateUserClientAsync();
        var uploaded = await UploadAsync(client, new TestPdf().Page().Build(), "scan.pdf");

        var failed = await WaitUntilDoneAsync(client, uploaded.Id);
        failed.Status.ShouldBe(DocumentStatus.Failed);
        failed.Error.ShouldNotBeNull().ShouldContain("No text found");

        var retried = await client.PostAsync($"/api/documents/{uploaded.Id}/retry", null, Ct);
        retried.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WaitUntilDoneAsync(client, uploaded.Id)).Status.ShouldBe(DocumentStatus.Failed);
    }

    [Fact]
    public async Task Documents_are_private_to_their_owner()
    {
        var owner = await factory.CreateUserClientAsync();
        var other = await factory.CreateUserClientAsync();
        var uploaded = await UploadAsync(owner, StudyNotes(), "notes.pdf");

        (await other.GetFromJsonAsync<List<DocumentSummary>>("/api/documents", Json, Ct)).ShouldBeEmpty();
        (await other.GetAsync($"/api/documents/{uploaded.Id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/documents/{uploaded.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_title_the_user_chose_survives_processing()
    {
        var client = await factory.CreateUserClientAsync();
        var uploaded = await UploadAsync(client, StudyNotes("Renaming"), "notes.pdf");

        var renamed = await client.PutAsJsonAsync($"/api/documents/{uploaded.Id}", new UpdateDocumentRequest("My notes", null), Json, Ct);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await WaitUntilDoneAsync(client, uploaded.Id)).Title.ShouldBe("My notes");
    }

    [Fact]
    public async Task Deleting_removes_the_document_and_its_chunks()
    {
        var client = await factory.CreateUserClientAsync();
        var uploaded = await UploadAsync(client, StudyNotes("Deletion"), "notes.pdf");
        await WaitUntilDoneAsync(client, uploaded.Id);

        var response = await client.DeleteAsync($"/api/documents/{uploaded.Id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<DocumentSummary>>("/api/documents", Json, Ct)).ShouldBeEmpty();
        (await ChunksAsync(uploaded.Id)).ShouldBeEmpty();
        (await client.GetAsync($"/api/documents/{uploaded.Id}/file", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/documents", form, Ct);
    }

    private static async Task<DocumentSummary> UploadAsync(HttpClient client, byte[] content, string fileName)
    {
        var response = await PostFileAsync(client, content, fileName);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DocumentSummary>(Json, Ct)).ShouldNotBeNull();
    }

    /// <summary>The worker runs in the background; poll like the web app does.</summary>
    private static async Task<DocumentSummary> WaitUntilDoneAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var documents = await client.GetFromJsonAsync<List<DocumentSummary>>("/api/documents", Json, Ct);
            var document = documents.ShouldNotBeNull().Single(d => d.Id == id);
            if (document.Status is DocumentStatus.Ready or DocumentStatus.Failed || DateTime.UtcNow > deadline)
            {
                return document;
            }

            await Task.Delay(200, Ct);
        }
    }

    private async Task<List<DocumentChunk>> ChunksAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
        return await db.DocumentChunks.IgnoreQueryFilters().Where(c => c.DocumentId == documentId).ToListAsync(Ct);
    }
}
