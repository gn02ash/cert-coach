using CertCoach.Application.Users;

namespace CertCoach.Api.Endpoints;

internal static class TokenEndpoints
{
    /// <summary>Token management is part of the browser API, so a token can never create or revoke tokens.</summary>
    public static RouteGroupBuilder MapTokenEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tokens").WithTags("Access tokens");

        group.MapGet("/", (ApiTokenService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListApiTokens");

        group.MapPost("/", async (CreateApiTokenRequest request, ApiTokenService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/tokens/{created.Details.Id}", created);
            })
            .WithName("CreateApiToken")
            .WithSummary("Create a personal access token for connecting an AI assistant over MCP. The token is returned once.");

        group.MapDelete("/{id:guid}", async (Guid id, ApiTokenService service, CancellationToken ct) =>
            {
                await service.RevokeAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("RevokeApiToken");

        return api;
    }
}
