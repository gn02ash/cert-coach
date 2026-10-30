using CertCoach.Application.Dashboard;
using CertCoach.Application.Plans;

namespace CertCoach.Api.Endpoints;

internal static class PlanEndpoints
{
    public static RouteGroupBuilder MapPlanEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", (DashboardService service, CancellationToken ct) => service.GetAsync(ct))
            .WithName("GetDashboard")
            .WithTags("Dashboard");

        var group = api.MapGroup("/plans").WithTags("Plans");

        group.MapPost("/preview", (PlanRequest request, PlanService service, CancellationToken ct) => service.PreviewAsync(request, ct))
            .WithName("PreviewPlan")
            .WithSummary("Generate a plan without saving it.");

        group.MapPost("/", async (PlanRequest request, PlanService service, CancellationToken ct) =>
            {
                var plan = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/plans/{plan.Id}", plan);
            })
            .WithName("CreatePlan");

        group.MapGet("/", (PlanService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListPlans");

        group.MapGet("/{planId:guid}", (Guid planId, PlanService service, CancellationToken ct) => service.GetAsync(planId, ct))
            .WithName("GetPlan");

        group.MapPost("/{planId:guid}/sessions/{sessionId:guid}/complete",
                (Guid planId, Guid sessionId, PlanService service, CancellationToken ct) => service.CompleteSessionAsync(planId, sessionId, ct))
            .WithName("CompleteSession");

        group.MapPost("/{planId:guid}/sessions/{sessionId:guid}/miss",
                (Guid planId, Guid sessionId, PlanService service, CancellationToken ct) => service.MissSessionAsync(planId, sessionId, ct))
            .WithName("MissSession")
            .WithSummary("Mark a session missed and reschedule the rest of the plan.");

        group.MapPost("/{planId:guid}/reschedule-overdue",
                (Guid planId, PlanService service, CancellationToken ct) => service.RescheduleOverdueAsync(planId, ct))
            .WithName("RescheduleOverdue")
            .WithSummary("Treat planned sessions from past days as missed and reschedule them.");

        group.MapPost("/{planId:guid}/archive", (Guid planId, PlanService service, CancellationToken ct) => service.ArchiveAsync(planId, ct))
            .WithName("ArchivePlan");

        group.MapDelete("/{planId:guid}", async (Guid planId, PlanService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(planId, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeletePlan");

        return api;
    }
}
