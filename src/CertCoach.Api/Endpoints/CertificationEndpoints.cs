using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;

namespace CertCoach.Api.Endpoints;

internal static class CertificationEndpoints
{
    public static RouteGroupBuilder MapCertificationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/outlines/parse", (ParseOutlineRequest request) => OutlineParser.Parse(request.Text))
            .WithName("ParseOutline")
            .WithTags("Certifications")
            .WithSummary("Parse a pasted skills outline into an editable draft.");

        api.MapPost("/outlines/parse-ai", (ParseOutlineRequest request, AiOutlineParser parser, CancellationToken ct) =>
                parser.ParseAsync(request.Text, ct))
            .WithName("ParseOutlineWithAi")
            .WithTags("Certifications")
            .WithSummary("Parse an outline in any layout with the chat model. Needs a chat provider.");

        var group = api.MapGroup("/certifications").WithTags("Certifications");

        group.MapGet("/", (CertificationService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListCertifications");

        group.MapGet("/{id:guid}", (Guid id, CertificationService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetCertification");

        group.MapPost("/", async (SaveCertificationRequest request, CertificationService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/certifications/{created.Id}", created);
            })
            .WithName("CreateCertification");

        group.MapPut("/{id:guid}", (Guid id, SaveCertificationRequest request, CertificationService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .WithName("UpdateCertification");

        group.MapDelete("/{id:guid}", async (Guid id, CertificationService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteCertification");

        return api;
    }
}
