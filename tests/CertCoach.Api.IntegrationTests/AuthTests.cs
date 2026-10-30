using System.Net;
using System.Net.Http.Json;
using CertCoach.Api.Auth;
using CertCoach.Application.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class AuthTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_api_requires_sign_in()
    {
        var response = await factory.CreateClient().GetAsync("/api/dashboard", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task State_changing_requests_need_the_csrf_header()
    {
        var client = await factory.CreateUserClientAsync();
        client.DefaultRequestHeaders.Remove(CsrfHeader);

        var response = await client.PostAsJsonAsync("/api/outlines/parse", new { text = "x" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_configured_providers_are_offered()
    {
        var providers = await factory.CreateClient().GetFromJsonAsync<List<AuthProvider>>("/auth/providers", Json, Ct);

        providers.ShouldNotBeNull().Select(p => p.Id).ShouldBe(["dev"]);
        (await NoRedirectClient().GetAsync("/auth/login/microsoft", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dev_login_sets_a_session_cookie_and_logout_clears_it()
    {
        var client = NoRedirectClient();

        var login = await client.GetAsync("/auth/login/dev?returnUrl=/plans", Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.ShouldBe("/plans");

        var me = await client.GetFromJsonAsync<UserProfile>("/auth/me", Json, Ct);
        me.ShouldNotBeNull().Id.ShouldBe(AuthSetup.DevUserId);
        (await client.GetAsync("/api/dashboard", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsync("/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/auth/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public async Task Login_never_redirects_off_site(string returnUrl)
    {
        var response = await NoRedirectClient().GetAsync($"/auth/login/dev?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);

        response.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task The_learner_time_zone_is_saved_and_used()
    {
        var client = NoRedirectClient();
        await client.GetAsync("/auth/login/dev", Ct);
        client.DefaultRequestHeaders.Add(CsrfHeader, "1");

        var updated = await client.PutAsJsonAsync("/auth/me/time-zone", new TimeZoneRequest("Europe/Paris"), Json, Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<UserProfile>("/auth/me", Json, Ct))!.TimeZone.ShouldBe("Europe/Paris");

        var invalid = await client.PutAsJsonAsync("/auth/me/time-zone", new TimeZoneRequest("Mars/Olympus"), Json, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await client.PutAsJsonAsync("/auth/me/time-zone", new TimeZoneRequest("UTC"), Json, Ct);
    }

    private HttpClient NoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
}
