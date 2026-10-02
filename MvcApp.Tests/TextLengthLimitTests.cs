using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>L12 — free-text fields have maximum lengths: over the limit is refused with a message (never silently cut), at the limit is stored whole.</summary>
public class TextLengthLimitTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public TextLengthLimitTests(AppFactory app) => _app = app;

    private async Task<HttpRequestMessage> JsonPostAsync(HttpClient client, string url, object body)
    {
        var token = await AppFactory.GetFormTokenAsync(client, "/admin/dashboard/users");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", token);
        return request;
    }

    private async Task<string> AddActivePlanAsync(string store)
    {
        await _app.SeedAsync(db =>
        {
            db.StoreActionPlans.Add(new StoreActionPlan { StoreName = store, Status = "Active", CreatedMonth = 3, CreatedYear = 2026 });
            return Task.CompletedTask;
        });
        return store;
    }

    private static string Path(string store, string action) => $"/api/store-action-plan/{Uri.EscapeDataString(store)}/{action}";

    // ── manual close reason ──────────────────────────────────────────────────

    [Fact]
    public async Task CloseReason_OverTheLimit_IsRefused_AndThePlanStaysOpen()
    {
        var admin = await _app.AdminClientAsync();
        var store = await AddActivePlanAsync("Close Over");

        var response = await admin.SendAsync(await JsonPostAsync(admin, Path(store, "close"), new { reason = new string('r', InputLimits.CloseReason + 1) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("too long", await response.Content.ReadAsStringAsync());
        Assert.Equal("Active", (await _app.QueryAsync(db => db.StoreActionPlans.AsNoTracking().SingleAsync(p => p.StoreName == store))).Status);
    }

    [Fact]
    public async Task CloseReason_AtTheLimit_IsStoredWhole()
    {
        var admin = await _app.AdminClientAsync();
        var store = await AddActivePlanAsync("Close Exact");
        var reason = new string('r', InputLimits.CloseReason);

        var response = await admin.SendAsync(await JsonPostAsync(admin, Path(store, "close"), new { reason }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await _app.QueryAsync(db => db.StoreActionPlans.AsNoTracking().SingleAsync(p => p.StoreName == store));
        Assert.Equal("Resolved", plan.Status);
        Assert.Equal(reason, plan.ManualCloseReason); // not truncated
    }

    [Fact]
    public async Task CloseReason_Normal_StillWorks()
    {
        var admin = await _app.AdminClientAsync();
        var store = await AddActivePlanAsync("Close Normal");

        var response = await admin.SendAsync(await JsonPostAsync(admin, Path(store, "close"), new { reason = "Handled with the area manager." }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── assignment name ──────────────────────────────────────────────────────

    [Fact]
    public async Task AssignedToName_OverTheLimit_IsRefused_AtTheLimitIsStoredWhole()
    {
        var admin = await _app.AdminClientAsync();
        var store = await AddActivePlanAsync("Assign");

        var tooLong = await admin.SendAsync(await JsonPostAsync(admin, Path(store, "assign"), new { assignedToName = new string('n', InputLimits.PersonName + 1) }));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("too long", await tooLong.Content.ReadAsStringAsync());

        var name = new string('n', InputLimits.PersonName);
        var ok = await admin.SendAsync(await JsonPostAsync(admin, Path(store, "assign"), new { assignedToName = name }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(name, (await _app.QueryAsync(db => db.StoreActionPlans.AsNoTracking().SingleAsync(p => p.StoreName == store))).AssignedToName);
    }

    // ── recommendation templates ─────────────────────────────────────────────

    [Fact]
    public async Task RecommendationTemplate_OverTheLimit_IsRefused_NormalTextStillSaves()
    {
        var admin = await _app.AdminClientAsync();
        var templates = await admin.GetFromJsonAsync<List<RecommendationTemplate>>("/api/settings/recommendation-templates");
        var first = templates!.First();
        object Body(string en, string ar) => new { signalCode = first.SignalCode, category = first.Category, index = first.Index, textEn = en, textAr = ar };

        var tooLong = await admin.SendAsync(await JsonPostAsync(admin, "/api/settings/recommendation-templates", Body(new string('e', InputLimits.RecommendationText + 1), "ok")));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("too long", await tooLong.Content.ReadAsStringAsync());

        var ok = await admin.SendAsync(await JsonPostAsync(admin, "/api/settings/recommendation-templates", Body("Check staffing for the next two weeks.", "راجع الجدولة للأسبوعين القادمين.")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ── notes (written by the store's own manager) ───────────────────────────

    [Fact]
    public async Task PlanNote_OverTheLimit_IsRefused_AtTheLimitIsStoredWhole_NormalNotesStillWork()
    {
        const string store = "Notes Store";
        const string email = "hm-notes@example.com";
        await AddActivePlanAsync(store);
        await _app.SeedAsync(db =>
        {
            db.StoreReferences.Add(new StoreReference { StoreName = store, Month = 3, Year = 2026, HeadManager = "Head Manager", HeadManagerEmail = email });
            return Task.CompletedTask;
        });
        await _app.AddUserAsync(email, "Head_Manager");
        var manager = await _app.SignInAsync(email, AppFactory.AdminPassword, admin: false);

        async Task<HttpResponseMessage> Post(string text)
        {
            var token = await AppFactory.GetFormTokenAsync(manager, "/home/dashboard/actioncenter");
            var request = new HttpRequestMessage(HttpMethod.Post, Path(store, "notes")) { Content = JsonContent.Create(new { noteText = text }) };
            request.Headers.Add("RequestVerificationToken", token);
            return await manager.SendAsync(request);
        }

        var normal = await Post("Visited the store, crew rota fixed.");
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);

        var tooLong = await Post(new string('x', InputLimits.NoteText + 1));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("too long", await tooLong.Content.ReadAsStringAsync());

        var exact = new string('y', InputLimits.NoteText);
        Assert.Equal(HttpStatusCode.OK, (await Post(exact)).StatusCode);

        var notes = await _app.QueryAsync(db => db.ActionPlanNotes.AsNoTracking().Select(n => n.NoteText).ToListAsync());
        Assert.Equal(2, notes.Count);
        Assert.Contains(exact, notes); // stored whole
    }

    // ── User-Agent in login history ──────────────────────────────────────────

    [Fact]
    public async Task UserAgent_IsClippedInLoginHistory_AndALongHeaderDoesNotBlockTheLogin()
    {
        await _app.AddUserAsync("ua-user@example.com", "User");
        var client = _app.NewClient();
        var longAgent = "Mozilla/5.0 " + new string('a', 6000);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", longAgent);
        var token = await AppFactory.GetFormTokenAsync(client, "/login");

        var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "ua-user@example.com", ["Password"] = AppFactory.AdminPassword, ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var stored = await _app.QueryAsync(db => db.LoginHistories.AsNoTracking().Where(l => l.Email == "ua-user@example.com").Select(l => l.UserAgent).SingleAsync());
        Assert.Equal(InputLimits.UserAgent, stored!.Length);
        Assert.StartsWith("Mozilla/5.0 aaa", stored);
    }

    [Fact]
    public void NormalUserAgents_AreNotTouched()
    {
        const string chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
        Assert.Equal(chrome, InputLimits.Clip(chrome, InputLimits.UserAgent));
        Assert.Null(InputLimits.Clip(null, InputLimits.UserAgent));
        Assert.Equal("", InputLimits.Clip("", InputLimits.UserAgent));
    }

    // ── user create / edit forms ─────────────────────────────────────────────

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void UserForms_RejectOverlongFields_AndAcceptNormalOnes()
    {
        var normal = new CreateUserViewModel { Email = "a.b@example.com", Phone = "+201012345678", AssignedName = "Ahmed Mohamed", Role = "User" };
        Assert.Empty(Validate(normal));

        var longEmail = new string('a', InputLimits.Email - "@example.com".Length + 1) + "@example.com";
        Assert.Contains(Validate(new CreateUserViewModel { Email = longEmail, Phone = "1", Role = "User" }), r => r.MemberNames.Contains("Email"));
        Assert.Contains(Validate(new CreateUserViewModel { Email = "a@example.com", Phone = new string('9', InputLimits.Phone + 1), Role = "User" }), r => r.MemberNames.Contains("Phone"));
        Assert.Contains(Validate(new CreateUserViewModel { Email = "a@example.com", Phone = "1", AssignedName = new string('n', InputLimits.PersonName + 1), Role = "User" }), r => r.MemberNames.Contains("AssignedName"));
        Assert.Contains(Validate(new EditUserViewModel { Email = "a@example.com", Phone = "1", AssignedName = new string('n', InputLimits.PersonName + 1), Role = "User" }), r => r.MemberNames.Contains("AssignedName"));

        var atLimit = new CreateUserViewModel { Email = new string('a', InputLimits.Email - "@example.com".Length) + "@example.com", Phone = new string('9', InputLimits.Phone), AssignedName = new string('n', InputLimits.PersonName), Role = "User" };
        Assert.Empty(Validate(atLimit));
    }

    [Fact]
    public async Task CreateUserForm_ShowsAMessage_ForAnOverlongName_AndStillCreatesNormalUsers()
    {
        var admin = await _app.AdminClientAsync();
        async Task<HttpResponseMessage> Post(string email, string name)
        {
            var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/createuser");
            return await admin.PostAsync("/admin/dashboard/createuser", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email, ["Phone"] = "+201000000123", ["AssignedName"] = name, ["Role"] = "User", ["__RequestVerificationToken"] = token,
            }));
        }

        var tooLong = await Post("long-name@example.com", new string('n', InputLimits.PersonName + 1));
        Assert.Equal(HttpStatusCode.OK, tooLong.StatusCode); // form shown again, with the validation message
        Assert.Contains("is too long (maximum 200 characters)", System.Net.WebUtility.HtmlDecode(await tooLong.Content.ReadAsStringAsync()));
        Assert.False(await _app.QueryAsync(db => db.Users.AnyAsync(u => u.Email == "long-name@example.com")));

        var ok = await Post("normal-name@example.com", "Ahmed Mohamed");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.True(await _app.QueryAsync(db => db.Users.AnyAsync(u => u.Email == "normal-name@example.com")));
    }
}
