using System.Net;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Dashboard;
using CertCoach.Application.Plans;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using Microsoft.AspNetCore.Mvc;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class StudyFlowTests(CertCoachApiFactory factory)
{
    private static readonly DateOnly Monday = DateOnly.FromDateTime(StartTime.UtcDateTime);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_endpoint_reports_the_database_as_healthy()
    {
        var response = await factory.CreateClient().GetAsync("/health", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pasting_an_outline_and_saving_it_creates_a_certification()
    {
        var client = factory.CreateUserClient();

        var created = await CreateSampleCertificationAsync(client);

        created.ExamCode.ShouldBe("DEMO-101");
        created.Domains.Count.ShouldBe(4);
        var list = await client.GetFromJsonAsync<List<CertificationSummary>>("/api/certifications", Json, Ct);
        list.ShouldNotBeNull().Single().ObjectiveCount.ShouldBe(21);
    }

    [Fact]
    public async Task Duplicate_exam_codes_are_rejected_with_409()
    {
        var client = factory.CreateUserClient();
        await CreateSampleCertificationAsync(client);

        var response = await client.PostAsJsonAsync("/api/certifications", await SampleRequestAsync(client), Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Previewing_a_plan_does_not_save_it()
    {
        var client = factory.CreateUserClient();
        var certification = await CreateSampleCertificationAsync(client);

        var preview = await PostAsync<PlanPreview>(client, "/api/plans/preview", PlanFor(certification));

        preview.SessionMinutes.ShouldBeInRange(30, 45);
        preview.Sessions.ShouldNotBeEmpty();
        preview.Sessions[0].Date.ShouldBe(Monday);
        (await client.GetFromJsonAsync<List<PlanSummary>>("/api/plans", Json, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_dashboard_shows_todays_session_and_tracks_progress()
    {
        var client = factory.CreateUserClient();
        var plan = await CreatePlanAsync(client);

        var dashboard = await GetDashboardAsync(client);
        var today = dashboard.Plans.Single().Today.Single();
        today.Kind.ShouldBe(SessionKind.Learn);
        today.Title.ShouldNotBeNullOrWhiteSpace();
        today.Objectives.ShouldNotBeEmpty();
        dashboard.Plans.Single().Upcoming.Count.ShouldBe(DashboardService.UpcomingDays);

        await PostAsync<PlanView>(client, $"/api/plans/{plan.Id}/sessions/{today.Id}/complete");

        var after = (await GetDashboardAsync(client)).Plans.Single();
        after.Progress.Completed.ShouldBe(1);
        after.Domains.Sum(d => d.ObjectivesStudied).ShouldBe(1);
    }

    [Fact]
    public async Task Missing_a_session_reschedules_its_objectives()
    {
        var client = factory.CreateUserClient();
        var plan = await CreatePlanAsync(client);
        var first = plan.Sessions[0];

        var updated = await PostAsync<PlanView>(client, $"/api/plans/{plan.Id}/sessions/{first.Id}/miss");

        updated.Sessions.Single(s => s.Id == first.Id).Status.ShouldBe(SessionStatus.Missed);
        updated.Sessions
            .Where(s => s.Status == SessionStatus.Planned)
            .ShouldContain(s => s.Objectives.Select(o => o.Id).SequenceEqual(first.Objectives.Select(o => o.Id)));
    }

    [Fact]
    public async Task Overdue_sessions_are_rolled_forward_on_request()
    {
        // A plan that started on Friday has three planned sessions before today (Monday).
        var client = factory.CreateUserClient();
        var certification = await CreateSampleCertificationAsync(client);
        var plan = await PostAsync<PlanView>(client, "/api/plans", PlanFor(certification) with { StartDate = Monday.AddDays(-3) });

        (await GetDashboardAsync(client)).Plans.Single().OverdueSessions.ShouldBe(3);

        var updated = await PostAsync<PlanView>(client, $"/api/plans/{plan.Id}/reschedule-overdue");

        updated.Sessions.Count(s => s.Status == SessionStatus.Missed).ShouldBe(3);
        (await GetDashboardAsync(client)).Plans.Single().OverdueSessions.ShouldBe(0);
    }

    [Fact]
    public async Task Only_one_active_plan_per_certification()
    {
        var client = factory.CreateUserClient();
        var plan = await CreatePlanAsync(client);

        var response = await client.PostAsJsonAsync("/api/plans", PlanFor(plan.CertificationId), Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_outline_is_locked_while_a_plan_is_active()
    {
        var client = factory.CreateUserClient();
        var plan = await CreatePlanAsync(client);
        var request = await SampleRequestAsync(client);
        var changed = request with { Outline = request.Outline.Take(2).ToList() };

        var locked = await client.PutAsJsonAsync($"/api/certifications/{plan.CertificationId}", changed, Json, Ct);
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await PostAsync<PlanView>(client, $"/api/plans/{plan.Id}/archive");
        var unlocked = await client.PutAsJsonAsync($"/api/certifications/{plan.CertificationId}", changed, Json, Ct);
        unlocked.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_settings_return_problem_details()
    {
        var client = factory.CreateUserClient();
        var certification = await CreateSampleCertificationAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/plans", PlanFor(certification) with { ExamDate = Monday.AddDays(-1) }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct);
        problem.ShouldNotBeNull().Detail.ShouldBe("The exam date must be after the start date.");
    }

    [Fact]
    public async Task Users_cannot_see_each_others_data()
    {
        var owner = factory.CreateUserClient();
        var plan = await CreatePlanAsync(owner);
        var stranger = factory.CreateUserClient();

        (await stranger.GetAsync($"/api/certifications/{plan.CertificationId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/plans/{plan.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/api/plans/{plan.Id}/sessions/{plan.Sessions[0].Id}/miss", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetDashboardAsync(stranger)).Plans.ShouldBeEmpty();
    }

    private static PlanRequest PlanFor(CertificationDetail certification) => PlanFor(certification.Id);

    private static PlanRequest PlanFor(Guid certificationId) =>
        new(certificationId, null, Monday.AddDays(42), 5m, Enum.GetValues<DayOfWeek>(), null);

    private static async Task<PlanView> CreatePlanAsync(HttpClient client)
    {
        var certification = await CreateSampleCertificationAsync(client);
        return await PostAsync<PlanView>(client, "/api/plans", PlanFor(certification));
    }

    private static async Task<CertificationDetail> CreateSampleCertificationAsync(HttpClient client) =>
        await PostAsync<CertificationDetail>(client, "/api/certifications", await SampleRequestAsync(client));

    private static async Task<SaveCertificationRequest> SampleRequestAsync(HttpClient client)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "DEMO-101-outline.md"), Ct);
        var draft = await PostAsync<OutlineDraft>(client, "/api/outlines/parse", new ParseOutlineRequest(text));
        return new SaveCertificationRequest(
            "DEMO-101", "Contoso AI Solutions Engineer", "Contoso", null, null, CertificationStatus.Active, null, draft.Domains);
    }

    private static async Task<DashboardView> GetDashboardAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardView>("/api/dashboard", Json, Ct)).ShouldNotBeNull();

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object? body = null)
        where T : class
    {
        var response = await client.PostAsJsonAsync(url, body, Json, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct)).ShouldNotBeNull();
    }
}
