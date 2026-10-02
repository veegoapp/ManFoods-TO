using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Mvc.Routing;
using MvcApp.Tests.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace MvcApp.Tests;

/// <summary>
/// M5 — business rule: the "User" role may use every normal project page, report and API (all stores, employee-level data),
/// and ONLY Data Management (uploads), Users and Settings are Admin-only. These tests hit the real server-side pipeline, so they
/// prove a direct URL request is refused, not merely that a menu item is hidden.
/// </summary>
public class AdminOnlyAreasAuthorizationTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    private readonly ITestOutputHelper _output;
    public AdminOnlyAreasAuthorizationTests(AppFactory app, ITestOutputHelper output) { _app = app; _output = output; }

    private static bool IsRefusal(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden ||
        (r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location?.OriginalString is "/adminlogin" or "/login");

    // The Admin portal sends a non-Admin to its own login page; the JSON APIs answer 403.
    private static void AssertRefused(HttpResponseMessage r, string what) =>
        Assert.True(IsRefusal(r), $"{what}: expected a redirect to login or 403 for a User, got {(int)r.StatusCode} {r.Headers.Location}");

    private async Task<(HttpClient client, string token)> UserWithTokenAsync()
    {
        var user = await _app.UserClientAsync();
        return (user, await AppFactory.GetFormTokenAsync(user, "/home/dashboard/turnover"));
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("RequestVerificationToken", token);
        return request;
    }

    // ── the three Admin-only areas: Admin can open them ──────────────────────

    [Theory]
    [InlineData("/admin/dashboard/uploads")]   // Data Management
    [InlineData("/admin/dashboard/users")]     // Users
    [InlineData("/admin/dashboard/settings")]  // Settings
    [InlineData("/admin/dashboard/createuser")]
    public async Task Admin_CanOpenTheAdminOnlyPages(string url)
    {
        var admin = await _app.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/settings/access-policy")]
    [InlineData("/api/settings/page-visibility")]
    [InlineData("/api/settings/recommendation-templates")]
    [InlineData("/api/settings/color-rules/turnover-total")]
    [InlineData("/api/action-plan-role/stores")]
    [InlineData("/api/action-plan-severity")]
    [InlineData("/api/action-plan-severity/history")]
    [InlineData("/admin/dashboard/background-jobs")]
    [InlineData("/admin/dashboard/download-template?type=bulk_users")]
    public async Task Admin_CanUseTheAdminOnlyApisAndDownloads(string url)
    {
        var admin = await _app.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }

    // ── …and a User cannot, by typing the URL ────────────────────────────────

    [Theory]
    [InlineData("/admin/dashboard/uploads")]
    [InlineData("/admin/dashboard/uploads?tab=jobproj")]
    [InlineData("/admin/dashboard/users")]
    [InlineData("/admin/dashboard/createuser")]
    [InlineData("/admin/dashboard/edituser/1")]
    [InlineData("/admin/dashboard/loginhistory/1")]
    [InlineData("/admin/dashboard/activitylogs")]
    [InlineData("/admin/dashboard/settings")]
    [InlineData("/admin/dashboard/background-jobs")]
    [InlineData("/admin/dashboard/download-template?type=bulk_users")]
    [InlineData("/admin/dashboard/download-template?type=active_employees")]
    [InlineData("/admin/dashboard/downloaduploadfile/1")]
    [InlineData("/admin/dashboard/preview-upload-file?id=1")]
    [InlineData("/admin/dashboard/turnover")]
    [InlineData("/admin/dashboard/export?reportType=turnover")]
    [InlineData("/admin/account/changepassword")]
    public async Task User_CannotOpenAdminPortalPages_ByDirectUrl(string url)
    {
        var user = await _app.UserClientAsync();
        AssertRefused(await user.GetAsync(url), url);
    }

    [Theory]
    [InlineData("GET", "/api/settings/access-policy")]
    [InlineData("GET", "/api/settings/page-visibility")]
    [InlineData("GET", "/api/settings/recommendation-templates")]
    [InlineData("GET", "/api/action-plan-role/stores")]
    [InlineData("GET", "/api/action-plan-severity")]
    [InlineData("GET", "/api/action-plan-severity/history")]
    [InlineData("GET", "/api/activity-logs")]
    [InlineData("POST", "/api/settings/access-policy")]
    [InlineData("POST", "/api/settings/page-visibility")]
    [InlineData("POST", "/api/settings/recommendation-templates")]
    [InlineData("POST", "/api/settings/color-rules/turnover-total")]
    [InlineData("POST", "/api/action-plan-role/Store%20A/set")]
    [InlineData("POST", "/api/action-plan-severity")]
    [InlineData("POST", "/api/store-action-plan/run-detection")]
    [InlineData("POST", "/api/store-action-plan/backfill-signals")]
    [InlineData("POST", "/api/store-action-plan/Store%20A/assign")]
    [InlineData("POST", "/api/store-action-plan/Store%20A/close")]
    public async Task User_CannotUseTheSettingsApis(string method, string url)
    {
        var (user, token) = await UserWithTokenAsync();
        // A well-formed body, so the refusal is the authorization one and not a model-binding error.
        object body = url.Contains("color-rules") ? new[] { new { upTo = (double?)null, color = "good" } } : new { };
        var response = await user.SendAsync(Json(new HttpMethod(method), url, token, body));
        AssertRefused(response, $"{method} {url}");
    }

    [Theory]
    [InlineData("/admin/dashboard/generatedefaultpasswords")]
    [InlineData("/admin/dashboard/uploadperioddata")]
    [InlineData("/admin/dashboard/updateperiodfile")]
    [InlineData("/admin/dashboard/uploadexitinterviews")]
    [InlineData("/admin/dashboard/uploadjobprojections")]
    [InlineData("/admin/dashboard/uploadcrewtrainers")]
    [InlineData("/admin/dashboard/uploadjobpayrollgroups")]
    [InlineData("/admin/dashboard/deleteuploadlog")]
    [InlineData("/admin/dashboard/createuser")]
    [InlineData("/admin/dashboard/edituser/1")]
    [InlineData("/admin/dashboard/deleteuser")]
    [InlineData("/admin/dashboard/uploadbulkusers")]
    [InlineData("/admin/dashboard/generatedefaultpassword")]
    [InlineData("/admin/dashboard/generateotp")]
    [InlineData("/admin/dashboard/generateadminotp")]
    [InlineData("/admin/dashboard/regeneraterecoverykey")]
    [InlineData("/admin/dashboard/background-jobs/abc/dismiss")]
    public async Task User_CannotSubmitTheDataManagementOrUserManagementActions(string url)
    {
        var (user, token) = await UserWithTokenAsync();
        var response = await user.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["id"] = "1" }));
        AssertRefused(response, $"POST {url}");
    }

    /// <summary>Walks every route the application registers in the Admin portal's dashboard controller (Data Management,
    /// Users, Settings and the pages next to them) and proves a User is refused on each — so a future action added there
    /// without protection fails this test.</summary>
    [Fact]
    public async Task EveryAdminPortalDashboardRoute_RefusesAUser()
    {
        var (user, token) = await UserWithTokenAsync();
        var routes = _app.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } d && d.RouteValues.TryGetValue("area", out var a) && a == "Admin" && d.ControllerName == "Dashboard")
            .ToList();
        Assert.True(routes.Count >= 40, $"expected the Admin dashboard routes to be discovered, found {routes.Count}");

        var checkedCount = 0;
        foreach (var endpoint in routes)
        {
            var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
            var url = BuildUrl(endpoint.RoutePattern, descriptor);
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            foreach (var method in methods)
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (method != "GET")
                {
                    request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
                    request.Headers.Add("RequestVerificationToken", token);
                }
                var response = await user.SendAsync(request);
                _output.WriteLine($"{method,-5} {url} -> {(int)response.StatusCode} {response.Headers.Location}");
                AssertRefused(response, $"{method} {url}");
                checkedCount++;
            }
        }
        Assert.True(checkedCount >= 40);
    }

    private static string BuildUrl(RoutePattern pattern, ControllerActionDescriptor descriptor)
    {
        var parts = new List<string>();
        foreach (var segment in pattern.PathSegments)
        {
            var text = string.Concat(segment.Parts.Select(p => p switch
            {
                RoutePatternLiteralPart l => l.Content,
                RoutePatternParameterPart { Name: "area" } => "Admin",
                RoutePatternParameterPart { Name: "controller" } => descriptor.ControllerName,
                RoutePatternParameterPart { Name: "action" } => descriptor.ActionName,
                RoutePatternParameterPart { IsOptional: true } => "",
                RoutePatternParameterPart => "1",
                _ => "",
            }));
            if (text.Length > 0) parts.Add(text);
        }
        return "/" + string.Join("/", parts);
    }

    // ── the rest of the project stays open to a User ─────────────────────────

    [Theory]
    [InlineData("/home/dashboard/turnover")]
    [InlineData("/home/dashboard/workforce")]
    [InlineData("/home/dashboard/comparisons")]
    [InlineData("/home/dashboard/retention")]
    [InlineData("/home/dashboard/stores")]
    [InlineData("/home/dashboard/exitinterviews")]
    [InlineData("/home/dashboard/ninetydayturnover")]
    [InlineData("/home/dashboard/earlywarning")]
    [InlineData("/home/dashboard/scorecard")]
    [InlineData("/home/dashboard/actioncenter")]
    [InlineData("/home/dashboard/workforceplanning")]
    [InlineData("/home/dashboard/reports")]
    [InlineData("/home/account/changepassword")]
    public async Task User_CanStillOpenEveryNormalProjectPage(string url)
    {
        var user = await _app.UserClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task NotificationBell_IsGone_ButPagesStillRender()
    {
        var user = await _app.UserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/notifications")).StatusCode);
        var html = await (await user.GetAsync("/home/dashboard/turnover")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("notifBtn", html);
        Assert.DoesNotContain("notifications.js", html);
        Assert.Contains("admin-global-right", html); // header (user menu) is still rendered
    }

    [Theory]
    [InlineData("/api/dashboard/kpis?month=3&year=2026")]
    [InlineData("/api/dashboard/store-comparison?month=3&year=2026")]
    [InlineData("/api/dashboard/stores")]
    [InlineData("/api/retention/milestones")]
    [InlineData("/api/ninety-day-turnover/early-leavers?month=3&year=2026")]
    [InlineData("/api/early-warning/watchlist?year=2026")]
    [InlineData("/api/exit-interviews/comments")]
    [InlineData("/api/scorecard?year=2026")]
    [InlineData("/api/workforce-planning/summary?year=2026&month=3")]
    [InlineData("/api/store-action-plan/action-center/stores")]
    [InlineData("/api/settings/color-rules/turnover-total")] // read-only thresholds every dashboard uses to colour its numbers
    public async Task User_CanStillUseTheNormalProjectApis(string url)
    {
        var user = await _app.UserClientAsync();
        var response = await user.GetAsync(url);
        Assert.True(response.StatusCode is HttpStatusCode.OK, $"{url} -> {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("/home/dashboard/export?reportType=turnover")]
    [InlineData("/home/dashboard/export?reportType=early-warning")]
    [InlineData("/home/dashboard/export?reportType=exit-interviews")]
    [InlineData("/home/dashboard/export?reportType=stores-overview&month=3&year=2026")]
    public async Task User_CanStillDownloadTheNormalReports(string url)
    {
        var user = await _app.UserClientAsync();
        var response = await user.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
    }

    // ── nothing else about authorization changed ─────────────────────────────

    [Theory]
    [InlineData("/admin/dashboard/users", "/adminlogin")]
    [InlineData("/admin/dashboard/uploads", "/adminlogin")]
    [InlineData("/admin/dashboard/settings", "/adminlogin")]
    [InlineData("/home/dashboard/turnover", "/login")]
    [InlineData("/api/dashboard/kpis?month=3&year=2026", "/login")]
    [InlineData("/api/settings/access-policy", "/login")]
    public async Task SignedOutVisitors_AreStillSentToTheLoginPage(string url, string login)
    {
        var anonymous = _app.NewClient();
        var response = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(login, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Admin_StillUsesTheAdminPortal_AndIsStillSentAwayFromTheUserPortal()
    {
        var admin = await _app.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/dashboard/turnover")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/dashboard/kpis?month=3&year=2026")).StatusCode);
        var homePortal = await admin.GetAsync("/home/dashboard/turnover");
        Assert.Equal(HttpStatusCode.Redirect, homePortal.StatusCode); // unchanged: an Admin uses the Admin portal
        Assert.Equal("/login", homePortal.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AdminOnlyApis_StillRefuseOtherNonAdminRoles()
    {
        await _app.AddUserAsync("ops-manager@example.com", "Operation_Manager");
        var manager = await _app.SignInAsync("ops-manager@example.com", AppFactory.AdminPassword, admin: false);
        var token = await AppFactory.GetFormTokenAsync(manager, "/home/dashboard/turnover");

        AssertRefused(await manager.GetAsync("/api/settings/access-policy"), "access-policy as Operation_Manager");
        AssertRefused(await manager.GetAsync("/admin/dashboard/users"), "users as Operation_Manager");
        AssertRefused(await manager.SendAsync(Json(HttpMethod.Post, "/api/store-action-plan/run-detection", token)), "run-detection as Operation_Manager");
    }
}
