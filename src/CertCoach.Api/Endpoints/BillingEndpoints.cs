using CertCoach.Application.Billing;

namespace CertCoach.Api.Endpoints;

internal static class BillingEndpoints
{
    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/billing").WithTags("Billing");

        group.MapGet("/allowance", (AiAllowance allowance, CancellationToken ct) => allowance.GetAsync(ct))
            .WithName("GetAiAllowance")
            .WithSummary("The learner's plan and how much of this month's AI allowance is used. Not enforced unless billing is on.");

        return api;
    }
}
