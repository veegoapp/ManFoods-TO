using System.Net;
using Microsoft.EntityFrameworkCore;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>M1 — regenerating every pending user's temporary password must be POST + anti-forgery only.</summary>
public class BulkPasswordRegenerationTests : IClassFixture<AppFactory>
{
    private const string Path = "/admin/dashboard/generatedefaultpasswords";
    private readonly AppFactory _app;
    public BulkPasswordRegenerationTests(AppFactory app) => _app = app;

    private async Task<int> AddPendingUserAsync(string email)
    {
        var user = await _app.AddUserAsync(email, "User", passwordHashOverride: null); // no password yet = pending activation
        return user.Id;
    }

    private Task<string?> HashOfAsync(int id) => _app.QueryAsync(async db => (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).PasswordHash);

    [Fact]
    public async Task Get_CannotTriggerTheRegeneration_AndChangesNothing()
    {
        var admin = await _app.AdminClientAsync();
        var id = await AddPendingUserAsync("pending-get@example.com");

        var response = await admin.GetAsync(Path);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Null(await HashOfAsync(id));
    }

    [Fact]
    public async Task Post_WithoutTheAntiForgeryToken_IsRejected_AndChangesNothing()
    {
        var admin = await _app.AdminClientAsync();
        var id = await AddPendingUserAsync("pending-notoken@example.com");

        var response = await admin.PostAsync(Path, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await HashOfAsync(id));
    }

    [Fact]
    public async Task Post_WithAWrongToken_IsRejected()
    {
        var admin = await _app.AdminClientAsync();
        await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users"); // cookie half is set; send a bogus form half
        var id = await AddPendingUserAsync("pending-badtoken@example.com");

        var response = await admin.PostAsync(Path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = "not-a-real-token" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await HashOfAsync(id));
    }

    [Fact]
    public async Task UsersPage_OffersThePostForm_NotALink()
    {
        var admin = await _app.AdminClientAsync();

        var html = await (await admin.GetAsync("/admin/dashboard/users")).Content.ReadAsStringAsync();

        Assert.Contains("<form method=\"post\" action=\"/admin/dashboard/generatedefaultpasswords\">", html);
        Assert.DoesNotContain("href=\"/admin/dashboard/generatedefaultpasswords\"", html);
    }

    [Fact]
    public async Task Post_WithAValidToken_StillGeneratesTheTemporaryPasswords_AsBefore()
    {
        var admin = await _app.AdminClientAsync();
        var id = await AddPendingUserAsync("pending-ok@example.com");
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users");

        var response = await admin.PostAsync(Path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("PK", System.Text.Encoding.ASCII.GetString((await response.Content.ReadAsByteArrayAsync())[..2]));
        var user = await _app.QueryAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.True(user.MustChangePassword);
        Assert.NotNull(user.TempPasswordExpiresAt);
    }

    [Fact]
    public async Task EveryStateChangingEndpoint_NowNeedsAToken_EvenWithoutItsOwnAttribute()
    {
        // The global AutoValidateAntiforgeryToken policy: a POST to the language switcher without a token is refused too.
        var client = _app.NewClient();
        var response = await client.PostAsync("/language/set", new FormUrlEncodedContent(new Dictionary<string, string> { ["lang"] = "ar" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
