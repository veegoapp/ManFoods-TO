using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Data;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>
/// Settings → Pages is now a page × role matrix (HR, User and the five operational roles), and the HR role exists and behaves exactly
/// like User. These tests prove: hiding a page for ONE role changes only that role (menu, direct URL, and the APIs/downloads behind it),
/// every API a page really calls stays open while that page is visible, new pages can be kept to HR until opened, and no store
/// scoping / Access setting / Admin permission changed.
/// </summary>
public class PerRolePageAccessTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public PerRolePageAccessTests(AppFactory app) => _app = app;

    private const string Hr = "hr-tests@example.com";
    private const string Ops = "ops-tests@example.com";

    private static Dictionary<string, Dictionary<string, bool>> Matrix(Func<string, string, bool> hidden) =>
        UserPages.All.ToDictionary(p => p.Key, p => UserPages.Roles.ToDictionary(r => r, r => hidden(p.Key, r)));

    private async Task SaveAsync(Func<string, string, bool> hidden)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPageVisibilityService>().SaveAsync(Matrix(hidden), "tests");
    }

    private Task ResetAsync() => SaveAsync((_, _) => false);

    private Task<HttpClient> HrAsync() => _app.RoleClientAsync(Hr, "HR");
    private Task<HttpClient> OpsAsync() => _app.RoleClientAsync(Ops, "Operation_Manager");

    private static string PageUrl(string pageKey) => "/home/dashboard/" + UserPages.All.Single(p => p.Key == pageKey).Action.ToLowerInvariant();
    private static async Task<string> BodyAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    // ── HR behaves exactly like User ─────────────────────────────────────────

    [Fact]
    public async Task Hr_IsUnrestrictedLikeUser_AndTheFiveOperationalRolesAreStillRestricted()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var access = new StoreAccessService(db);

        Assert.False(access.IsRestrictedRole("HR"));
        Assert.False(access.IsRestrictedRole("User"));
        Assert.False(access.IsRestrictedRole("Admin"));
        Assert.Null(await access.GetAccessibleStoreNamesAsync("HR", "hr@example.com")); // every store
        Assert.Null(await access.GetOwnStoreNamesAsync("HR", "hr@example.com"));
        Assert.True(await access.CanAccessStoreAsync("HR", "hr@example.com", "Any Store"));

        foreach (var restricted in new[] { "Operation_Manager", "Operation_Consultant", "Head_Manager", "Senior_Operation_Consultant", "Operation_Director" })
            Assert.True(access.IsRestrictedRole(restricted), restricted);
        Assert.True(access.IsRestrictedRole("Something_Else")); // an unknown role still gets no access
        Assert.Equal(5, access.RestrictedRoles.Count);          // HR was not added to the store-email matching roles
    }

    [Fact]
    public void Hr_IsARecognizedRole_AndAdminCanStillOnlyBeAssignedBySuperAdmin()
    {
        Assert.Contains("HR", UserManagementPolicy.ValidRoles);
        Assert.True(UserManagementPolicy.IsValidRole("hr")); // case-insensitive like the others
        Assert.Equal("HR", UserManagementPolicy.NormalizeRole("hr"));
        Assert.False(UserManagementPolicy.IsAdminRole("HR"));
        Assert.True(UserManagementPolicy.CanAssignRole("ops-admin@example.com", "HR"));      // any Admin may create an HR user, like a User
        Assert.False(UserManagementPolicy.CanAssignRole("ops-admin@example.com", "Admin"));  // unchanged
    }

    [Fact]
    public async Task Hr_SignsInToTheUserPortal_SeesEveryNormalPageAndApi_LikeUser()
    {
        await ResetAsync();
        var hr = await HrAsync();

        foreach (var page in UserPages.All)
            Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync(PageUrl(page.Key))).StatusCode);
        foreach (var url in new[] { "/api/dashboard/kpis?month=3&year=2026", "/api/early-warning/watchlist?year=2026", "/api/exit-interviews/comments",
                                    "/api/retention/milestones", "/api/store-action-plan/action-center/stores", "/api/settings/color-rules/turnover-total" })
            Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync("/home/dashboard/export?reportType=turnover")).StatusCode);
    }

    [Fact]
    public async Task Hr_CannotReachTheAdminOnlyAreas_ByDirectUrl()
    {
        var hr = await HrAsync();
        foreach (var url in new[] { "/admin/dashboard/uploads", "/admin/dashboard/users", "/admin/dashboard/settings", "/api/settings/access-policy",
                                    "/api/settings/page-visibility", "/api/settings/recommendation-templates", "/admin/dashboard/download-template?type=bulk_users" })
        {
            var r = await hr.GetAsync(url);
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden || (r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location?.OriginalString is "/adminlogin" or "/login"), $"{url} -> {(int)r.StatusCode}");
        }
    }

    // ── per-role hiding ──────────────────────────────────────────────────────

    [Fact]
    public async Task HidingAPageForOneRole_ChangesOnlyThatRole_MenuAndDirectUrl()
    {
        await SaveAsync((page, role) => page == "scorecard" && role == "HR");
        var hr = await HrAsync();
        var ops = await OpsAsync();
        var user = await _app.UserClientAsync();

        var hrMenu = await BodyAsync(await hr.GetAsync("/home/dashboard/turnover"));
        Assert.DoesNotContain("href=\"/home/dashboard/scorecard\"", hrMenu);
        Assert.Contains("href=\"/home/dashboard/retention\"", hrMenu);
        var hrDirect = await hr.GetAsync("/home/dashboard/scorecard");
        Assert.Equal(HttpStatusCode.Redirect, hrDirect.StatusCode);
        Assert.Equal("/home/dashboard/workforce", hrDirect.Headers.Location?.OriginalString);

        foreach (var other in new[] { user, ops })
        {
            Assert.Contains("href=\"/home/dashboard/scorecard\"", await BodyAsync(await other.GetAsync("/home/dashboard/turnover")));
            Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/home/dashboard/scorecard")).StatusCode);
        }
        await ResetAsync();
    }

    [Fact]
    public async Task HidingAPageForARole_AlsoClosesItsApi_ForThatRoleOnly_AndOnlyWhenNoVisiblePageNeedsIt()
    {
        // The retention API is shared by Retention, Comparisons and Stores: it stays open while any of them is visible…
        await SaveAsync((page, role) => page == "retention" && role == "HR");
        var hr = await HrAsync();
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync("/api/retention/milestones")).StatusCode);

        // …and is closed to that role (only) once all three are hidden.
        await SaveAsync((page, role) => role == "HR" && page is "retention" or "comparisons" or "stores");
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync("/api/retention/milestones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync("/api/retention/stores")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.UserClientAsync()).GetAsync("/api/retention/milestones")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await OpsAsync()).GetAsync("/api/retention/milestones")).StatusCode);
        // other pages' APIs and the shared lookups are untouched
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync("/api/early-warning/watchlist?year=2026")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync("/api/dashboard/available-periods")).StatusCode);
        await ResetAsync();
    }

    [Fact]
    public async Task SinglePageApis_AreClosedAsSoonAsThatPageIsHidden()
    {
        await SaveAsync((page, role) => role == "HR" && page is "hiringforecast" or "crewtrainers" or "workforceplanning" or "earlywarning" or "reports");
        var hr = await HrAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync("/api/workforce-planning/hiring-forecast?year=2026")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync("/api/workforce-planning/crew-trainers?year=2026&month=3")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync("/api/workforce-planning/summary?year=2026&month=3")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync("/api/workforce-planning/store-fill?year=2026&month=3")).StatusCode); // the Stores page still needs it
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync("/api/early-warning/watchlist?year=2026")).StatusCode);             // Stores page still visible
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.GetAsync("/home/dashboard/export?reportType=turnover")).StatusCode);   // downloads too
        Assert.Equal(HttpStatusCode.Redirect, (await hr.GetAsync("/home/dashboard/reports")).StatusCode);

        var user = await _app.UserClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/workforce-planning/hiring-forecast?year=2026")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/home/dashboard/export?reportType=turnover")).StatusCode);
        await ResetAsync();
    }

    [Fact]
    public async Task Admin_IsNeverAffected_ByAnyPageSetting()
    {
        await SaveAsync((page, _) => page != "workforce"); // everything hidden from every role except one page
        var admin = await _app.AdminClientAsync();

        foreach (var url in new[] { "/admin/dashboard/retention", "/admin/dashboard/earlywarning", "/api/retention/milestones", "/api/early-warning/watchlist?year=2026",
                                    "/api/workforce-planning/hiring-forecast?year=2026", "/admin/dashboard/export?reportType=turnover" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        await ResetAsync();
    }

    // ── safety net: hiding other pages never breaks a visible page's own APIs ─

    // The endpoints each page's script really calls (taken from its Views/Shared/Dashboard/_*Content.cshtml).
    private static readonly Dictionary<string, string[]> PageApis = new()
    {
        ["workforce"] = new[] { "/api/dashboard/kpis", "/api/dashboard/store-headcount-breakdown", "/api/dashboard/oc-om-analysis", "/api/dashboard/headcount-trend",
            "/api/dashboard/headcount-by-tenure", "/api/dashboard/headcount-by-payroll-group", "/api/dashboard/headcount-by-job-title", "/api/dashboard/gender-breakdown" },
        ["workforceplanning"] = new[] { "/api/workforce-planning/summary", "/api/workforce-planning/store-jobs?store=A" },
        ["hiringforecast"] = new[] { "/api/workforce-planning/hiring-forecast" },
        ["crewtrainers"] = new[] { "/api/workforce-planning/crew-trainers" },
        ["turnover"] = new[] { "/api/exit-interviews/reasons", "/api/dashboard/kpis", "/api/dashboard/turnover-trend", "/api/dashboard/turnover-by-payroll-group",
            "/api/dashboard/turnover-by-job-title", "/api/dashboard/trend-matrix", "/api/dashboard/store-comparison", "/api/dashboard/smart-insights",
            "/api/dashboard/oc-om-analysis", "/api/dashboard/gender-breakdown" },
        ["ninetyday"] = new[] { "/api/ninety-day-turnover/trend-matrix", "/api/ninety-day-turnover/trend", "/api/ninety-day-turnover/stores",
            "/api/ninety-day-turnover/operation-managers", "/api/ninety-day-turnover/kpi", "/api/ninety-day-turnover/early-leavers" },
        ["comparisons"] = new[] { "/api/retention/milestones", "/api/ninety-day-turnover/store-comparison", "/api/ninety-day-turnover/kpi",
            "/api/exit-interviews/sentiment-summary", "/api/exit-interviews/reasons", "/api/dashboard/store-comparison", "/api/dashboard/kpis" },
        ["retention"] = new[] { "/api/retention/time-to-first-resignation", "/api/retention/tenure-distribution-by-store", "/api/retention/tenure-distribution",
            "/api/retention/stores", "/api/retention/store-retention-ranking", "/api/retention/operation-managers", "/api/retention/monthly-hiring-volume",
            "/api/retention/by-job-title", "/api/retention/by-gender", "/api/retention/average-tenure-by-store", "/api/retention/active-tenure-curve", "/api/retention/milestones" },
        ["exitinterviews"] = new[] { "/api/exit-interviews/filters", "/api/exit-interviews/available-periods", "/api/exit-interviews/reasons", "/api/exit-interviews/comments" },
        ["earlywarning"] = new[] { "/api/early-warning/summary", "/api/early-warning/stores", "/api/early-warning/watchlist" },
        ["scorecard"] = new[] { "/api/scorecard", "/api/scorecard/rollup", "/api/scorecard/leaders", "/api/scorecard/leader-history?leader=x" },
        ["actioncenter"] = new[] { "/api/store-action-plan/action-center/health/summary", "/api/store-action-plan/action-center/health/stores",
            "/api/store-action-plan/action-center/detail?store=A", "/api/store-action-plan/action-center/signal-history?store=A",
            "/api/store-action-plan/action-center/monthly-turnover?store=A", "/api/store-action-plan/action-center/health/detail?store=A" },
        ["stores"] = new[] { "/api/dashboard/store-comparison", "/api/workforce-planning/store-fill", "/api/store-action-plan/action-center/stores", "/api/early-warning/watchlist",
            "/api/workforce-planning/store-plan?store=A", "/api/store-action-plan/action-center/signal-history?store=A", "/api/store-action-plan/action-center/detail?store=A",
            "/api/ninety-day-turnover/kpi", "/api/exit-interviews/sentiment-summary", "/api/exit-interviews/would-return", "/api/retention/tenure-distribution",
            "/api/dashboard/kpis", "/api/dashboard/turnover-by-tenure", "/api/dashboard/store-leader-tracking?store=A", "/api/dashboard/headcount-by-job-title",
            "/api/early-warning/summary", "/api/scorecard/leader-profile?leader=x" },
        ["reports"] = new[] { "/home/dashboard/export?reportType=turnover" },
    };

    [Fact]
    public async Task EveryPage_StillGetsItsOwnApis_WhenItIsTheOnlyVisiblePage()
    {
        Assert.Equal(UserPages.All.Select(p => p.Key).OrderBy(k => k), PageApis.Keys.OrderBy(k => k)); // a new page must list its APIs here
        var hr = await HrAsync();

        foreach (var page in UserPages.All)
        {
            await SaveAsync((p, role) => role == "HR" && p != page.Key); // only this page visible to HR
            Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync(PageUrl(page.Key))).StatusCode);
            foreach (var api in PageApis[page.Key])
            {
                var url = api + (api.Contains('?') ? "&" : "?") + "month=3&year=2026";
                var response = await hr.GetAsync(url);
                Assert.True(response.StatusCode != HttpStatusCode.Forbidden && response.StatusCode != HttpStatusCode.Redirect,
                    $"'{page.Key}' is the only visible page but its API {url} answered {(int)response.StatusCode}");
            }
        }
        await ResetAsync();
    }

    [Fact]
    public async Task EveryPageApi_IsOpenForAllRoles_ByDefault()
    {
        await ResetAsync();
        var ops = await OpsAsync();
        foreach (var (page, apis) in PageApis)
            foreach (var api in apis)
            {
                var url = api + (api.Contains('?') ? "&" : "?") + "month=3&year=2026";
                var r = await ops.GetAsync(url);
                Assert.True(r.StatusCode != HttpStatusCode.Forbidden && r.StatusCode != HttpStatusCode.Redirect, $"{page}: {url} -> {(int)r.StatusCode}");
            }
    }

    // ── pages that are still under review ────────────────────────────────────

    [Fact]
    public async Task NewPage_IsHiddenFromEveryoneButHr_UntilTickedForARole()
    {
        var pages = new List<UserPages.Page>(UserPages.All) { new("newpage", "NewPage", "Nav_NewPage", UnderReview: true) };
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var svc = new PageVisibilityService(db, new MemoryCache(new MemoryCacheOptions()), pages);

        Assert.DoesNotContain("newpage", await svc.GetHiddenAsync("HR"));
        foreach (var role in UserPages.Roles.Where(r => r != "HR"))
            Assert.Contains("newpage", await svc.GetHiddenAsync(role));
        Assert.DoesNotContain("workforce", await svc.GetHiddenAsync("User")); // ordinary pages are unaffected

        // the Admin opens it for User and the five operational roles
        var open = UserPages.Roles.Where(r => r != "HR").ToDictionary(r => r, _ => false);
        await svc.SaveAsync(new Dictionary<string, Dictionary<string, bool>> { ["newpage"] = open }, "admin");
        foreach (var role in UserPages.Roles) Assert.DoesNotContain("newpage", await svc.GetHiddenAsync(role));
        Assert.Equal(6, await db.PageVisibilities.CountAsync(p => p.PageKey == "newpage")); // stored explicitly, so a later default change cannot flip it

        // and closes it again for just one role
        await svc.SaveAsync(new Dictionary<string, Dictionary<string, bool>> { ["newpage"] = new() { ["Head_Manager"] = true } }, "admin");
        Assert.Contains("newpage", await svc.GetHiddenAsync("Head_Manager"));
        Assert.DoesNotContain("newpage", await svc.GetHiddenAsync("Operation_Director"));
    }

    [Fact]
    public void TodaysPages_AreNotUnderReview() => Assert.DoesNotContain(UserPages.All, p => p.UnderReview);

    // ── Settings → Pages (Admin) ─────────────────────────────────────────────

    [Fact]
    public async Task SettingsPagesApi_ReturnsTheWholeMatrix_AndRefusesToEmptyARole()
    {
        await ResetAsync();
        var admin = await _app.AdminClientAsync();

        var matrix = System.Text.Json.JsonDocument.Parse(await BodyAsync(await admin.GetAsync("/api/settings/page-visibility"))).RootElement;
        Assert.Equal(UserPages.All.Count, matrix.EnumerateObject().Count());
        Assert.Equal(UserPages.Roles.OrderBy(r => r), matrix.GetProperty("workforce").EnumerateObject().Select(p => p.Name).OrderBy(r => r));

        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/settings");
        async Task<HttpResponseMessage> Post(object hidden)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings/page-visibility") { Content = System.Net.Http.Json.JsonContent.Create(new { hidden }) };
            request.Headers.Add("RequestVerificationToken", token);
            return await admin.SendAsync(request);
        }

        var emptyHr = UserPages.All.ToDictionary(p => p.Key, _ => new Dictionary<string, bool> { ["HR"] = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(emptyHr)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(new Dictionary<string, Dictionary<string, bool>> { ["scorecard"] = new() { ["HR"] = true } })).StatusCode);
        Assert.Contains("scorecard", await _app.QueryAsync(async db => (await db.PageVisibilities.Where(p => p.Role == "HR" && p.IsHidden).Select(p => p.PageKey).ToListAsync())));
        await ResetAsync();
    }

    [Fact]
    public async Task SettingsPage_ShowsOneColumnPerRole_IncludingHr()
    {
        var admin = await _app.AdminClientAsync();
        var html = System.Net.WebUtility.HtmlDecode(await BodyAsync(await admin.GetAsync("/admin/dashboard/settings")));
        foreach (var role in UserPages.Roles) Assert.Contains($"\"key\":\"{role}\"", html);
        Assert.Contains("\"label\":\"HR\"", html);
    }

    // ── the migration keeps today's visibility exactly ───────────────────────

    [Fact]
    public void Migration_ExpandsOldRowsToTheSameRoles_AndKeepsEverythingElse()
    {
        var root = _app.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var sql = File.ReadAllText(Path.Combine(root, "scripts", "migrate.sql"));
        var block = sql[sql.IndexOf("-- ── page_visibility", StringComparison.Ordinal)..sql.IndexOf("-- ── seed users", StringComparison.Ordinal)];

        // the roles it expands each old row to are exactly the roles Settings → Pages manages
        var values = Regex.Match(block, @"CROSS JOIN \(VALUES (.*?)\) AS r\(role\)", RegexOptions.Singleline).Groups[1].Value;
        var roles = Regex.Matches(values, @"'([^']+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(UserPages.Roles.OrderBy(r => r), roles.OrderBy(r => r));

        Assert.Contains("PRIMARY KEY (page_key, role)", block);
        Assert.Contains("p.role = ''", block);                       // only old (role-less) rows are expanded…
        Assert.Contains("SELECT p.page_key, r.role, p.is_hidden", block); // …keeping their hidden/visible value
        Assert.Contains("DELETE FROM dbo.page_visibility WHERE role = '';", block);
        Assert.Contains("COL_LENGTH('dbo.page_visibility', 'role') IS NULL", block); // safe to re-run
        Assert.DoesNotContain("page_access_config", block);         // the Access table is not touched
    }

    // ── Access / data scoping untouched ──────────────────────────────────────

    [Fact]
    public async Task AccessSettings_AndStoreScoping_AreUntouched_ByThePageSettings()
    {
        await SaveAsync((page, role) => page == "earlywarning" && role == "HR");
        var before = await _app.QueryAsync(db => db.PageAccessConfigs.AsNoTracking().CountAsync());
        var admin = await _app.AdminClientAsync();

        var access = await admin.GetAsync("/api/settings/access-policy");
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        Assert.Equal(before, await _app.QueryAsync(db => db.PageAccessConfigs.AsNoTracking().CountAsync())); // saving page visibility wrote nothing to page_access_config
        await ResetAsync();
    }
}
