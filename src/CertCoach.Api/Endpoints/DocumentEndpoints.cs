using System.Net.Mime;
using CertCoach.Application.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace CertCoach.Api.Endpoints;

internal static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/documents").WithTags("Documents");

        group.MapGet("/", (DocumentService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListDocuments");

        group.MapPost("/", async (IFormFile file, [FromForm] Guid? certificationId, DocumentService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var created = await service.UploadAsync(content, file.FileName, certificationId, ct);
                return TypedResults.Created($"/api/documents/{created.Id}", created);
            })
            .WithName("UploadDocument")
            .WithSummary("Upload a PDF. It is processed in the background; poll the list for its status.")
            .Accepts<IFormFile>("multipart/form-data")
            // The X-CSRF header filter on the /api group protects this endpoint, not form antiforgery tokens.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(DocumentService.MaxUploadBytes + 1024 * 1024));

        group.MapPut("/{id:guid}", (Guid id, UpdateDocumentRequest request, DocumentService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .WithName("UpdateDocument");

        group.MapPost("/{id:guid}/retry", (Guid id, DocumentService service, CancellationToken ct) => service.RetryAsync(id, ct))
            .WithName("RetryDocument")
            .WithSummary("Process a failed document again.");

        group.MapGet("/{id:guid}/file", async (Guid id, DocumentService service, HttpContext http, CancellationToken ct) =>
            {
                var file = await service.OpenAsync(id, ct);

                // Inline, so the browser's PDF viewer opens it and #page=N links from citations work.
                http.Response.Headers[HeaderNames.ContentDisposition] =
                    new ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
                return TypedResults.Stream(file.Content, MediaTypeNames.Application.Pdf, enableRangeProcessing: true);
            })
            .WithName("GetDocumentFile")
            .Produces(StatusCodes.Status200OK, contentType: MediaTypeNames.Application.Pdf);

        group.MapDelete("/{id:guid}", async (Guid id, DocumentService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteDocument");

        return api;
    }
}
