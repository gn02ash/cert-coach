using CertCoach.Application.Practice;
using CertCoach.Application.Practice.Generation;

namespace CertCoach.Api.Endpoints;

internal static class PracticeEndpoints
{
    public static RouteGroupBuilder MapPracticeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/practice/items").WithTags("Practice");

        group.MapGet("/", (Guid certificationId, Guid? objectiveId, PracticeService service, CancellationToken ct) =>
                service.ListAsync(certificationId, objectiveId, ct))
            .WithName("ListPracticeItems")
            .WithSummary("A certification's practice questions and flashcards, optionally for one objective.");

        group.MapPost("/", async (CreatePracticeItemRequest request, PracticeService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/practice/items/{created.Id}", created);
            })
            .WithName("CreatePracticeItem")
            .WithSummary("Write your own question or flashcard.");

        group.MapPut("/{id:guid}", (Guid id, UpdatePracticeItemRequest request, PracticeService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .WithName("UpdatePracticeItem")
            .WithSummary("Edit an item. Its review schedule is kept.");

        group.MapPut("/{id:guid}/status", (Guid id, SetPracticeItemStatusRequest request, PracticeService service, CancellationToken ct) =>
                service.SetStatusAsync(id, request, ct))
            .WithName("SetPracticeItemStatus")
            .WithSummary("Suspend an item to leave it out of reviews and exams, or make it active again.");

        group.MapDelete("/{id:guid}", async (Guid id, PracticeService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeletePracticeItem");

        var runs = api.MapGroup("/practice/generations").WithTags("Practice");

        runs.MapPost("/", async (GenerateItemsRequest request, GenerationService service, CancellationToken ct) =>
            {
                var run = await service.StartAsync(request, ct);
                return TypedResults.Accepted($"/api/practice/generations/{run.Id}", run);
            })
            .WithName("StartGeneration")
            .WithSummary("Write questions and flashcards in the background for a certification, a domain or an objective.")
            .WithDescription("With a chat provider, items are written from your documents with citations, or from the model's knowledge when no document covers an objective. Offline, they are fill-in-the-blank items from your documents.");

        runs.MapGet("/", (Guid certificationId, GenerationService service, CancellationToken ct) => service.ListAsync(certificationId, ct))
            .WithName("ListGenerations")
            .WithSummary("The most recent generation runs for a certification, newest first.");

        runs.MapGet("/{id:guid}", (Guid id, GenerationService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetGeneration");

        runs.MapPost("/{id:guid}/cancel", (Guid id, GenerationService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithName("CancelGeneration")
            .WithSummary("Stop a run before its next objective. Items already written are kept.");

        return api;
    }
}
