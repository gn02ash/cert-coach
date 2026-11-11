using CertCoach.Application.Careers;
using Microsoft.AspNetCore.Mvc;

namespace CertCoach.Api.Endpoints;

internal static class CareerEndpoints
{
    public static RouteGroupBuilder MapCareerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/career").WithTags("Career");

        group.MapGet("/profile", (CareerService service, CancellationToken ct) => service.GetProfileAsync(ct))
            .WithName("GetCareerProfile");

        group.MapPut("/profile", (SaveProfileRequest request, CareerService service, CancellationToken ct) => service.SaveProfileAsync(request, ct))
            .WithName("SaveCareerProfile")
            .WithSummary("Save the profile recommendations are based on: skills, roles, experience, certifications held, target role.");

        group.MapDelete("/profile", async (CareerService service, CancellationToken ct) =>
            {
                await service.DeleteProfileAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteCareerProfile");

        group.MapPost("/cv/text", (ReadCvTextRequest request, CareerService service, CancellationToken ct) => service.ReadCvAsync(request.Text, ct))
            .WithName("ReadCvText")
            .WithSummary("Read a pasted CV into a draft profile. The CV is not stored.")
            .WithDescription("With a chat provider the text is sent to it to be read; without one, known skills, job titles and exam codes are matched.");

        group.MapPost("/cv/file", async (IFormFile file, CareerService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await service.ReadCvPdfAsync(content, ct);
            })
            .WithName("ReadCvFile")
            .WithSummary("Read a CV uploaded as a PDF into a draft profile. The file is read in memory and not stored.")
            .Accepts<IFormFile>("multipart/form-data")
            // The X-CSRF header filter on the /api group protects this endpoint, not form antiforgery tokens.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(CareerService.MaxCvBytes + (1024 * 1024)));

        group.MapGet("/recommendations", (CareerService service, CancellationToken ct) => service.RecommendAsync(ct))
            .WithName("GetRecommendations")
            .WithSummary("Certifications from the built-in catalogue ranked for the saved profile, with the reasons for each score.");

        group.MapPost("/recommendations/advice", (CareerService service, CancellationToken ct) => service.AdviseAsync(ct))
            .WithName("GetRecommendationAdvice")
            .WithSummary("The chat model's order and reasons for the recommended certifications. Needs a chat provider.");

        return api;
    }
}
