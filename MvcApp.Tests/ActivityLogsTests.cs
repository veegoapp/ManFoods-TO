using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

internal static class ActivityTestHelpers
{
    public static DateTime Utc(string iso) => DateTime.SpecifyKind(DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);

    public static ActivityLog Row(string action, DateTime at, bool success = true, string? email = "someone@example.com", int? target = null, string? reason = null, string? details = null, string? ip = null) => new()
    {
        Action = action, Category = ActivityActions.CategoryOf(action), OccurredAt = at, Success = success, UserEmail = email,
        TargetUserId = target, Reason = reason, Details = details, IpAddress = ip,
    };

    /// <summary>Signs in the clients first (a sign-in is itself an activity), then empties the table.</summary>
    public static async Task PrepareAsync(AppFactory app)
    {
        await app.SuperAdminClientAsync();
        await app.AdminClientAsync();
        await ResetAsync(app);
    }

    public static Task ResetAsync(AppFactory app) => app.SeedAsync(async db => db.ActivityLogs.RemoveRange(await db.ActivityLogs.ToListAsync()));
    public static Task SeedAsync(AppFactory app, params ActivityLog[] rows) => app.SeedAsync(db => { db.ActivityLogs.AddRange(rows); return Task.CompletedTask; });
    public static Task<int> CountAsync(AppFactory app, Func<IQueryable<ActivityLog>, IQueryable<ActivityLog>>? where = null) =>
        app.QueryAsync(db => (where == null ? db.ActivityLogs : where(db.ActivityLogs)).CountAsync());

    public static async Task<HttpResponseMessage> PostJsonAsync(AppFactory app, HttpClient client, string url, object body, string tokenPage = "/admin/dashboard/users")
    {
        var token = await AppFactory.GetFormTokenAsync(client, tokenPage);
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

/// <summary>Activity Logs is for the Super Admin account only: page, feed and Clear History all refuse an ordinary Admin,
/// a User and an anonymous caller on the server, and the refusals are recorded.</summary>
public class ActivityLogsAccessTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ActivityLogsAccessTests(AppFactory app) => _app = app;

    [Fact]
    public async Task SuperAdmin_CanOpenThePageAndTheFeed()
    {
        var sa = await _app.SuperAdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync("/admin/dashboard/activitylogs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync("/api/activity-logs")).StatusCode);
    }

    [Fact]
    public async Task OrdinaryAdmin_IsRefusedEverywhere_ByServerSideChecks()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app, ActivityTestHelpers.Row(ActivityActions.LoginSuccess, DateTime.UtcNow.AddMinutes(-5)));
        var admin = await _app.AdminClientAsync();   // an Admin, but not admin@mcd.com

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/admin/dashboard/activitylogs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/activity-logs?search=secretterm")).StatusCode);
        var body = new { range = "all", types = "all", password = AppFactory.AdminPassword };   // even with a correct password
        Assert.Equal(HttpStatusCode.Forbidden, (await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/activity-logs/clear/preview", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/activity-logs/clear", body)).StatusCode);

        // nothing was deleted, and the refusals are recorded (path only, never the query string)
        Assert.Equal(1, await ActivityTestHelpers.CountAsync(_app, q => q.Where(l => l.Action == ActivityActions.LoginSuccess)));
        var denied = await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.Action == ActivityActions.AccessDenied).ToListAsync());
        Assert.True(denied.Count >= 4);
        Assert.All(denied, d => Assert.False(d.Success));
        Assert.Contains(denied, d => d.Details == "GET /api/activity-logs");
        Assert.DoesNotContain(denied, d => (d.Details ?? "").Contains("secretterm"));
        Assert.All(denied, d => Assert.Equal(AppFactory.AdminEmail, d.UserEmail));
    }

    [Fact]
    public async Task User_AndAnonymous_AreRefused()
    {
        var user = await _app.UserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/activity-logs")).StatusCode);
        var page = await user.GetAsync("/admin/dashboard/activitylogs");
        Assert.True(page.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
        Assert.Equal(HttpStatusCode.Forbidden, (await ActivityTestHelpers.PostJsonAsync(_app, user, "/api/activity-logs/clear", new { range = "all", types = "all", password = AppFactory.AdminPassword }, "/home/account/changepassword")).StatusCode);

        var anon = _app.NewClient();
        Assert.True((await anon.GetAsync("/api/activity-logs")).StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
        Assert.True((await anon.GetAsync("/admin/dashboard/activitylogs")).StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SidebarLink_IsShownToTheSuperAdminOnly()
    {
        var sa = await _app.SuperAdminClientAsync();
        var admin = await _app.AdminClientAsync();
        var saHtml = await (await sa.GetAsync("/admin/dashboard/users")).Content.ReadAsStringAsync();
        var adminHtml = await (await admin.GetAsync("/admin/dashboard/users")).Content.ReadAsStringAsync();
        Assert.Contains("href=\"/admin/dashboard/activitylogs\"", saHtml);
        Assert.DoesNotContain("activitylogs", adminHtml);
    }
}

/// <summary>The feed: newest first, localized, filtered and paged in SQL, Cairo time, no secrets.</summary>
public class ActivityLogsFeedTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ActivityLogsFeedTests(AppFactory app) => _app = app;
    private static DateTime T(int minutesAgo) => DateTime.UtcNow.AddMinutes(-minutesAgo);

    private async Task<JsonElement> GetAsync(string query, HttpClient? client = null)
    {
        client ??= await _app.SuperAdminClientAsync();
        var response = await client.GetAsync("/api/activity-logs" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ActivityTestHelpers.JsonAsync(response);
    }

    [Fact]
    public async Task Feed_ShowsEveryKindOfActivity_NewestFirst_WithSummary_AndTargetEmail()
    {
        var target = await _app.AddUserAsync("feed-target@example.com", "User");
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app,
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, T(60), email: "a@example.com", ip: "10.0.0.1"),
            ActivityTestHelpers.Row(ActivityActions.LoginFailed, T(50), false, "a@example.com", reason: "wrong-password"),
            ActivityTestHelpers.Row(ActivityActions.LoginUnknown, T(40), false, "Ghost.Person@Example.com", reason: "unknown-account"),
            ActivityTestHelpers.Row(ActivityActions.LoginBlocked, T(35), false, "a@example.com", reason: "locked-out"),
            ActivityTestHelpers.Row(ActivityActions.Upload, T(30), true, "boss@example.com", details: "resignations; 3/2026"),
            ActivityTestHelpers.Row(ActivityActions.UserCreate, T(20), true, "boss@example.com", target.Id, details: "role User"),
            ActivityTestHelpers.Row(ActivityActions.AccessDenied, T(10), false, "boss@example.com", reason: "forbidden", details: "GET /api/activity-logs"));

        var root = await GetAsync("");
        var summary = root.GetProperty("summary");
        Assert.Equal(7, summary.GetProperty("total").GetInt32());
        Assert.Equal(3, summary.GetProperty("successful").GetInt32());
        Assert.Equal(4, summary.GetProperty("failed").GetInt32());
        Assert.Equal(3, summary.GetProperty("adminActions").GetInt32());   // upload + create user + denied admin action

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "access.denied", "user.create", "upload", "login-blocked", "login-unknown", "login-failed", "login-success" },
            items.Select(i => i.GetProperty("action").GetString()));
        Assert.Equal("Unauthorized / Blocked Admin Action", items[0].GetProperty("actionLabel").GetString());
        Assert.Equal("feed-target@example.com", items[1].GetProperty("target").GetString());
        Assert.Equal("Ghost.Person@Example.com", items[4].GetProperty("user").GetString());          // the email exactly as typed
        Assert.Equal("No account with this email", items[4].GetProperty("description").GetString());
        Assert.Equal("10.0.0.1", items[6].GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Filters_NarrowByActionResultSearchAndCairoDay()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app,
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-01-15T22:30:00Z"), email: "alpha@example.com"),    // Cairo 2026-01-16 00:30
            ActivityTestHelpers.Row(ActivityActions.LoginFailed, ActivityTestHelpers.Utc("2026-01-16T21:30:00Z"), false, "alpha@example.com"),      // Cairo 2026-01-16 23:30
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-01-16T22:30:00Z"), email: "beta@example.com"),     // Cairo 2026-01-17 00:30 (next day)
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-01-15T21:30:00Z"), email: "beta@example.com"),     // Cairo 2026-01-15 23:30 (previous day)
            ActivityTestHelpers.Row(ActivityActions.Upload, ActivityTestHelpers.Utc("2026-01-16T10:00:00Z"), email: "alpha@example.com"));

        async Task<int> Total(string q) => (await GetAsync(q)).GetProperty("totalItems").GetInt32();
        Assert.Equal(5, await Total(""));
        Assert.Equal(3, await Total("?search=ALPHA"));                                   // case-insensitive, by user
        Assert.Equal(3, await Total("?action=login"));
        Assert.Equal(1, await Total("?action=failed-login"));
        Assert.Equal(1, await Total("?action=upload"));
        Assert.Equal(4, await Total("?status=success"));
        Assert.Equal(1, await Total("?status=failed"));
        Assert.Equal(3, await Total("?from=2026-01-16&to=2026-01-16"));                  // Cairo calendar day: the two around midnight + the upload
        Assert.Equal(1, await Total("?from=2026-01-17"));                                // only the row at Cairo 2026-01-17 00:30
        Assert.Equal(5, await Total("?action=%27%3B--&status=bogus"));                   // unknown filter values are ignored
    }

    [Fact]
    public async Task Times_AreShownInCairoTime_WhateverTheStoredUtcIs()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app,
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-01-15T10:00:00Z")),    // winter: UTC+2
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-07-01T22:30:00Z")));   // summer (DST): UTC+3

        var items = (await GetAsync("")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("2026-07-02 01:30:00", items[0].GetProperty("timeDisplay").GetString());
        Assert.Equal("2026-01-15 12:00:00", items[1].GetProperty("timeDisplay").GetString());
        // the raw instant stays UTC
        Assert.Equal(ActivityTestHelpers.Utc("2026-01-15T10:00:00Z"), items[1].GetProperty("time").GetDateTime().ToUniversalTime());

        var sa = await _app.SuperAdminClientAsync();
        var html = await (await sa.GetAsync("/admin/dashboard/activitylogs")).Content.ReadAsStringAsync();
        Assert.Contains("item.timeDisplay", html);          // the page prints the server's Cairo string …
        Assert.DoesNotContain("toLocaleString", html);      // … and never formats with the viewer's clock or zone
    }

    [Fact]
    public async Task Paging_ReturnsOnePageAtATime_AndClampsOutOfRangePages()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app, Enumerable.Range(0, 60).Select(i => ActivityTestHelpers.Row(ActivityActions.LoginSuccess, T(1000 + i))).ToArray());

        var first = await GetAsync("");
        Assert.Equal(60, first.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, first.GetProperty("totalPages").GetInt32());
        Assert.Equal(ActivityLogService.PageSize, first.GetProperty("items").GetArrayLength());
        Assert.Equal(10, (await GetAsync("?page=3")).GetProperty("items").GetArrayLength());
        var beyond = await GetAsync("?page=99");
        Assert.Equal(3, beyond.GetProperty("page").GetInt32());
        Assert.Equal(1, (await GetAsync("?page=-4")).GetProperty("page").GetInt32());
        var p1 = first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()).ToHashSet();
        Assert.Empty(p1.Intersect((await GetAsync("?page=2")).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString())));
    }

    [Fact]
    public async Task Labels_AreLocalized_ForArabic()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        var arabic = await _app.SignInAsync(MvcApp.Services.SuperAdminPolicy.SuperAdminEmail, AppFactory.AdminPassword, admin: true);
        var token = await AppFactory.GetFormTokenAsync(arabic, "/admin/dashboard/users");
        await arabic.PostAsync("/language/set", new FormUrlEncodedContent(new Dictionary<string, string> { ["lang"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token }));
        await ActivityTestHelpers.ResetAsync(_app);   // after that sign-in, which is itself an activity
        await ActivityTestHelpers.SeedAsync(_app, ActivityTestHelpers.Row(ActivityActions.LoginUnknown, T(5), false, "x@example.com", reason: "unknown-account"));

        var item = (await GetAsync("", arabic)).GetProperty("items")[0];
        Assert.Equal("دخول بحساب غير موجود", item.GetProperty("actionLabel").GetString());
        var html = WebUtility.HtmlDecode(await (await arabic.GetAsync("/admin/dashboard/activitylogs")).Content.ReadAsStringAsync());
        Assert.Contains("سجل النشاط", html);
        Assert.Contains("مسح السجل", html);   // the Clear History button
    }

    [Fact]
    public async Task Feed_NeverCarriesSecrets_AndTheWriterCleansAndClipsWhatItIsGiven()
    {
        await ActivityTestHelpers.ResetAsync(_app);
        using (var scope = _app.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IActivityLogWriter>();
            await writer.LogAsync(new ActivityEntry { Action = ActivityActions.LoginUnknown, Success = false, UserEmail = "evil\r\nINJECTED line@example.com", Reason = new string('r', 500), Details = new string('d', 5000) });
        }
        var row = (await _app.QueryAsync(db => db.ActivityLogs.ToListAsync())).Single();
        Assert.Equal("evilINJECTED line@example.com", row.UserEmail);      // control characters removed
        Assert.Equal(100, row.Reason!.Length);
        Assert.Equal(1000, row.Details!.Length);

        // the table has no column that could hold a secret
        var columns = typeof(ActivityLog).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.DoesNotContain(columns, c => c.Contains("password") || c.Contains("hash") || c.Contains("otp") || c.Contains("token") || c.Contains("secret") || c.Contains("key"));
    }
}

/// <summary>Sign-in attempts are all recorded — including an unknown account with the full email typed — without
/// changing the message the person sees, and without storing the password.</summary>
public class LoginActivityTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public LoginActivityTests(AppFactory app) => _app = app;

    private async Task<string> PostLoginAsync(string path, string email, string password)
    {
        var client = _app.NewClient();
        var token = await AppFactory.GetFormTokenAsync(client, path);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token }));
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private Task<List<ActivityLog>> RowsAsync(string email) =>
        _app.QueryAsync(db => db.ActivityLogs.Where(l => l.UserEmail == email).OrderBy(l => l.Id).ToListAsync());

    [Fact]
    public async Task UnknownAccount_IsRecordedWithTheFullEmail_AndTheVisibleMessageIsUnchanged()
    {
        const string typed = "Nobody.Here@Example.com";
        var html = await PostLoginAsync("/login", typed, "Typed-Password-123!");
        Assert.Contains("Invalid email or password", html);

        var row = Assert.Single(await RowsAsync(typed));
        Assert.Equal(ActivityActions.LoginUnknown, row.Action);
        Assert.Equal(ActivityCategories.Login, row.Category);
        Assert.False(row.Success);
        Assert.Equal("unknown-account", row.Reason);
        Assert.Equal("Home", row.Portal);
        Assert.Null(row.ActorUserId);

        // the password is not stored anywhere in the log
        var all = await _app.QueryAsync(db => db.ActivityLogs.ToListAsync());
        Assert.DoesNotContain(all, l => string.Join('|', l.UserEmail, l.Details, l.Reason, l.UserAgent, l.Portal).Contains("Typed-Password-123!"));
    }

    [Fact]
    public async Task KnownAccount_WrongPassword_Success_WrongPortal_TempPasswordExpired_AndLogout_AreRecorded()
    {
        var user = await _app.AddUserAsync("activity-user@example.com", "User");
        var html = await PostLoginAsync("/login", "activity-user@example.com", "Wrong-Password-999!");
        Assert.Contains("Invalid email or password", html);

        var good = await _app.SignInAsync("activity-user@example.com", AppFactory.AdminPassword, admin: false);
        var token = await AppFactory.GetFormTokenAsync(good, "/home/account/changepassword");
        await good.PostAsync("/home/account/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        await _app.AddUserAsync("activity-admin2@example.com", "Admin");
        Assert.Contains("Admin accounts must use the admin portal", await PostLoginAsync("/login", "activity-admin2@example.com", AppFactory.AdminPassword));

        var expired = await _app.AddUserAsync("activity-expired@example.com", "User");
        await _app.SeedAsync(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Id == expired.Id);
            u.MustChangePassword = true; u.TempPasswordExpiresAt = DateTime.UtcNow.AddHours(-1);
        });
        Assert.Contains("temporary password has expired", await PostLoginAsync("/login", "activity-expired@example.com", AppFactory.AdminPassword));

        var rows = await RowsAsync("activity-user@example.com");
        Assert.Equal(new[] { ActivityActions.LoginFailed, ActivityActions.LoginSuccess, ActivityActions.Logout }, rows.Select(r => r.Action));
        Assert.Equal("wrong-password", rows[0].Reason);
        Assert.Equal(user.Id, rows[0].ActorUserId);
        Assert.True(rows[1].Success);
        Assert.Equal("Home", rows[1].Portal);

        var wrongPortal = Assert.Single(await RowsAsync("activity-admin2@example.com"));
        Assert.Equal((ActivityActions.LoginFailed, "wrong-portal", false), (wrongPortal.Action, wrongPortal.Reason, wrongPortal.Success));   // not a "successful login"

        var temp = Assert.Single(await RowsAsync("activity-expired@example.com"));
        Assert.Equal("temp-password-expired", temp.Reason);
    }
}

public class BlockedLoginActivityTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public BlockedLoginActivityTests(AppFactory app) => _app = app;

    [Fact]
    public async Task AfterTooManyWrongPasswords_TheBlockedAttemptIsRecorded()
    {
        await _app.AddUserAsync("blocked-user@example.com", "User");
        for (var i = 0; i < 6; i++)
        {
            var client = _app.NewClient();
            var token = await AppFactory.GetFormTokenAsync(client, "/login");
            var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = "blocked-user@example.com", ["Password"] = "Nope-Nope-123!", ["__RequestVerificationToken"] = token }));
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains(i < 5 ? "Invalid email or password" : "Too many failed attempts", html);   // visible behaviour unchanged
        }
        var rows = await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.UserEmail == "blocked-user@example.com").OrderBy(l => l.Id).ToListAsync());
        Assert.Equal(5, rows.Count(r => r.Action == ActivityActions.LoginFailed));
        var blocked = Assert.Single(rows, r => r.Action == ActivityActions.LoginBlocked);
        Assert.Equal("locked-out", blocked.Reason);
        Assert.False(blocked.Success);
    }
}

/// <summary>Admin actions (user changes, credentials, uploads) land in the same table, with a target user id and no secrets.</summary>
public class AdminActionActivityTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public AdminActionActivityTests(AppFactory app) => _app = app;

    private async Task<HttpResponseMessage> PostAsync(string path, Dictionary<string, string> form)
    {
        var admin = await _app.AdminClientAsync();
        form["__RequestVerificationToken"] = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users");
        return await admin.PostAsync(path, new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task UserLifecycle_RoleChange_AndDeniedAttempts_AreRecorded_WithoutSecrets()
    {
        var email = "activity-target@example.com";
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync("/admin/dashboard/createuser", new() { ["Email"] = email, ["Phone"] = "+201055566677", ["Role"] = "User" })).StatusCode);
        var id = await _app.QueryAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        await PostAsync($"/admin/dashboard/edituser/{id}", new() { ["Email"] = email, ["Phone"] = "+201055566677", ["Role"] = "HR" });
        var pwdBody = await (await PostAsync("/admin/dashboard/generatedefaultpassword", new() { ["id"] = id.ToString() })).Content.ReadAsStringAsync();
        var password = JsonDocument.Parse(pwdBody).RootElement.GetProperty("password").GetString()!;
        await PostAsync("/admin/dashboard/generateadminotp", new() { ["id"] = id.ToString() });                 // not the Super Admin → denied
        await PostAsync("/admin/dashboard/regeneraterecoverykey", new() { ["password"] = "Whatever-123!" });     // not the Super Admin → denied
        await PostAsync("/admin/dashboard/deleteuser", new() { ["id"] = id.ToString() });

        var rows = await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.Category == ActivityCategories.Admin).OrderBy(l => l.Id).ToListAsync());
        Assert.Contains(rows, r => r.Action == ActivityActions.UserCreate && r.TargetUserId == id && r.Success && r.UserEmail == AppFactory.AdminEmail);
        Assert.Contains(rows, r => r.Action == ActivityActions.UserUpdate && r.TargetUserId == id);
        var change = Assert.Single(rows, r => r.Action == ActivityActions.RoleChange);
        Assert.Equal("User -> HR", change.Details);
        Assert.Equal(id, change.TargetUserId);
        Assert.Contains(rows, r => r.Action == ActivityActions.PasswordGenerate && r.TargetUserId == id);
        Assert.Contains(rows, r => r.Action == ActivityActions.OtpGenerateAdmin && !r.Success && r.Reason == "not-super-admin");
        Assert.Contains(rows, r => r.Action == ActivityActions.RecoveryKeyRegenerate && !r.Success && r.Reason == "not-super-admin");
        Assert.Contains(rows, r => r.Action == ActivityActions.UserDelete && r.TargetUserId == id && r.Success);

        var text = string.Join('\n', rows.Select(r => string.Join('|', r.UserEmail, r.Details, r.Reason, r.UserAgent)));
        Assert.DoesNotContain(password, text);
        Assert.DoesNotContain("Whatever-123!", text);
        Assert.DoesNotContain(email, text);                                    // the target is an id, not an email
    }

    [Fact]
    public async Task RejectedUpload_IsRecorded_WithTypeAndPeriodOnly()
    {
        var form = new MultipartFormDataContent();
        var admin = await _app.AdminClientAsync();
        form.Add(new StringContent(await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/uploads")), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("MZ not excel")), "File", "secret-name.xlsx");
        await admin.PostAsync("/admin/dashboard/uploadjobpayrollgroups", form);

        var row = await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.Action == ActivityActions.Upload).SingleAsync());
        Assert.False(row.Success);
        Assert.Equal("invalid-file", row.Reason);
        Assert.Equal("job_payroll_groups; all", row.Details);
        Assert.DoesNotContain("secret-name", row.Details);
        Assert.Equal(ActivityCategories.Data, row.Category);
    }
}

/// <summary>Clear History deletes exactly the chosen range and type, only for the Super Admin, only with their current password.</summary>
public class ClearActivityHistoryScopeTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ClearActivityHistoryScopeTests(AppFactory app) => _app = app;
    private static DateTime T(int minutesAgo) => DateTime.UtcNow.AddMinutes(-minutesAgo);

    private async Task SeedAsync()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app,
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, T(30), details: "login-recent"),
            ActivityTestHelpers.Row(ActivityActions.LoginFailed, T(180), false, details: "login-3h"),
            ActivityTestHelpers.Row(ActivityActions.LoginUnknown, ActivityTestHelpers.Utc("2026-01-10T12:00:00Z"), false, details: "login-jan10"),
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-01-20T12:00:00Z"), details: "login-jan20"),
            ActivityTestHelpers.Row(ActivityActions.Upload, T(20), details: "upload-recent"),
            ActivityTestHelpers.Row(ActivityActions.Upload, ActivityTestHelpers.Utc("2026-01-10T12:00:00Z"), details: "upload-jan10"),
            ActivityTestHelpers.Row(ActivityActions.UserCreate, T(10), details: "admin-recent"),
            ActivityTestHelpers.Row(ActivityActions.UserDelete, ActivityTestHelpers.Utc("2026-01-10T12:00:00Z"), details: "admin-jan10"));
    }

    private async Task<HashSet<string?>> RemainingAsync() =>
        (await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.Action != ActivityActions.ActivityLogsClear).Select(l => l.Details).ToListAsync())).ToHashSet();

    private async Task<HttpResponseMessage> ClearAsync(object body) =>
        await ActivityTestHelpers.PostJsonAsync(_app, await _app.SuperAdminClientAsync(), "/api/activity-logs/clear", body);

    [Fact]
    public async Task ByType_OnlyTheChosenTypeIsDeleted()
    {
        await SeedAsync();
        var response = await ClearAsync(new { range = "all", types = "login", password = AppFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, (await ActivityTestHelpers.JsonAsync(response)).GetProperty("deleted").GetInt32());
        Assert.Equal(new HashSet<string?> { "upload-recent", "upload-jan10", "admin-recent", "admin-jan10" }, await RemainingAsync());

        await SeedAsync();
        await ClearAsync(new { range = "all", types = "data", password = AppFactory.AdminPassword });
        Assert.DoesNotContain("upload-recent", await RemainingAsync());
        Assert.DoesNotContain("upload-jan10", await RemainingAsync());
        Assert.Equal(6, (await RemainingAsync()).Count);

        await SeedAsync();
        await ClearAsync(new { range = "all", types = "admin", password = AppFactory.AdminPassword });
        Assert.Equal(new HashSet<string?> { "login-recent", "login-3h", "login-jan10", "login-jan20", "upload-recent", "upload-jan10" }, await RemainingAsync());
    }

    [Fact]
    public async Task ByRelativeRange_OnlyTheLastHourIsDeleted()
    {
        await SeedAsync();
        await ClearAsync(new { range = "last-hour", types = "all", password = AppFactory.AdminPassword });
        Assert.Equal(new HashSet<string?> { "login-3h", "login-jan10", "login-jan20", "upload-jan10", "admin-jan10" }, await RemainingAsync());

        await SeedAsync();
        await ClearAsync(new { range = "last-24h", types = "login", password = AppFactory.AdminPassword });   // range AND type
        Assert.Equal(new HashSet<string?> { "login-jan10", "login-jan20", "upload-recent", "upload-jan10", "admin-recent", "admin-jan10" }, await RemainingAsync());
    }

    [Fact]
    public async Task ByCustomCairoDays_AndType_NothingOutsideIsTouched()
    {
        await SeedAsync();
        // "Delete Login Logs from Jan 9 to Jan 15": only login-jan10
        var response = await ClearAsync(new { range = "custom", fromDay = "2026-01-09", toDay = "2026-01-15", types = "login", password = AppFactory.AdminPassword });
        Assert.Equal(1, (await ActivityTestHelpers.JsonAsync(response)).GetProperty("deleted").GetInt32());
        var left = await RemainingAsync();
        Assert.DoesNotContain("login-jan10", left);
        Assert.Equal(7, left.Count);
        Assert.Contains("upload-jan10", left);     // same days, different type
        Assert.Contains("login-jan20", left);      // same type, different days
    }
}

/// <summary>Clear History: the whole range, the password check, preview and validation (a separate class: the endpoint is rate limited per IP).</summary>
public class ClearActivityHistoryGuardTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ClearActivityHistoryGuardTests(AppFactory app) => _app = app;
    private static DateTime T(int minutesAgo) => DateTime.UtcNow.AddMinutes(-minutesAgo);

    private async Task SeedAsync()
    {
        await ActivityTestHelpers.PrepareAsync(_app);
        await ActivityTestHelpers.SeedAsync(_app,
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, T(30), details: "login-recent"),
            ActivityTestHelpers.Row(ActivityActions.LoginFailed, T(180), false, details: "login-3h"),
            ActivityTestHelpers.Row(ActivityActions.LoginUnknown, ActivityTestHelpers.Utc("2026-01-10T12:00:00Z"), false, details: "login-jan10"),
            ActivityTestHelpers.Row(ActivityActions.LoginSuccess, ActivityTestHelpers.Utc("2026-01-20T12:00:00Z"), details: "login-jan20"),
            ActivityTestHelpers.Row(ActivityActions.Upload, T(20), details: "upload-recent"),
            ActivityTestHelpers.Row(ActivityActions.Upload, ActivityTestHelpers.Utc("2026-01-10T12:00:00Z"), details: "upload-jan10"),
            ActivityTestHelpers.Row(ActivityActions.UserCreate, T(10), details: "admin-recent"),
            ActivityTestHelpers.Row(ActivityActions.UserDelete, ActivityTestHelpers.Utc("2026-01-10T12:00:00Z"), details: "admin-jan10"));
    }

    private async Task<HashSet<string?>> RemainingAsync() =>
        (await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.Action != ActivityActions.ActivityLogsClear).Select(l => l.Details).ToListAsync())).ToHashSet();

    private async Task<HttpResponseMessage> ClearAsync(object body) =>
        await ActivityTestHelpers.PostJsonAsync(_app, await _app.SuperAdminClientAsync(), "/api/activity-logs/clear", body);

    [Fact]
    public async Task AllTime_AllTypes_DeletesEverything_ButTheClearingItselfIsRecorded()
    {
        await SeedAsync();
        var response = await ClearAsync(new { range = "all", types = "all", password = AppFactory.AdminPassword });
        Assert.Equal(8, (await ActivityTestHelpers.JsonAsync(response)).GetProperty("deleted").GetInt32());
        var rows = await _app.QueryAsync(db => db.ActivityLogs.ToListAsync());
        var record = Assert.Single(rows);
        Assert.Equal(ActivityActions.ActivityLogsClear, record.Action);
        Assert.True(record.Success);
        Assert.Equal(MvcApp.Services.SuperAdminPolicy.SuperAdminEmail, record.UserEmail);
        Assert.Contains("deleted=8", record.Details);
        Assert.DoesNotContain(AppFactory.AdminPassword, record.Details);
    }

    [Fact]
    public async Task WrongOrMissingPassword_DeletesNothing_AndIsRecorded_WithoutThePassword()
    {
        await SeedAsync();
        foreach (var body in new object[]
        {
            new { range = "all", types = "all", password = "Not-The-Password-1!" },
            new { range = "all", types = "all", password = "" },
            new { range = "all", types = "all" },
        })
            Assert.Equal(HttpStatusCode.Forbidden, (await ClearAsync(body)).StatusCode);

        Assert.Equal(8, (await RemainingAsync()).Count);
        var failures = await _app.QueryAsync(db => db.ActivityLogs.Where(l => l.Action == ActivityActions.ActivityLogsClear).ToListAsync());
        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => { Assert.False(f.Success); Assert.Equal("incorrect-password", f.Reason); });
        var text = string.Join('|', failures.Select(f => f.Details + f.Reason + f.UserEmail));
        Assert.DoesNotContain("Not-The-Password-1!", text);
    }

    [Fact]
    public async Task PreviewCountsWithoutDeleting_AndInvalidRequestsAreRejected()
    {
        await SeedAsync();
        var sa = await _app.SuperAdminClientAsync();
        var preview = await ActivityTestHelpers.PostJsonAsync(_app, sa, "/api/activity-logs/clear/preview", new { range = "last-hour", types = "login" });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var json = await ActivityTestHelpers.JsonAsync(preview);
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        Assert.NotNull(json.GetProperty("fromDisplay").GetString());
        Assert.Equal(8, (await RemainingAsync()).Count);   // nothing deleted

        var invalid = new object[]
        {
            new { range = "bogus", types = "all", password = AppFactory.AdminPassword },
            new { range = "all", types = "everything", password = AppFactory.AdminPassword },
            new { range = "custom", types = "all", password = AppFactory.AdminPassword },                                         // custom without dates
            new { range = "custom", fromDay = "2026-02-01", toDay = "2026-01-01", types = "all", password = AppFactory.AdminPassword }, // from after to
        };
        foreach (var body in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await ActivityTestHelpers.PostJsonAsync(_app, sa, "/api/activity-logs/clear", body)).StatusCode);
        Assert.Equal(8, (await RemainingAsync()).Count);
    }
}
