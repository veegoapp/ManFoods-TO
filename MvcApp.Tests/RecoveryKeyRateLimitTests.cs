using System.Net;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The Admin Recovery Key check has its own rate-limit policy: one shared budget for the whole app (not per
/// client IP, which an attacker can rotate), so the CPU cost of the BCrypt check cannot be multiplied. A separate
/// class on purpose: the budget is per host, and each test class gets its own.</summary>
public class RecoveryKeyRateLimitTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public RecoveryKeyRateLimitTests(AppFactory app) => _app = app;

    private static FormUrlEncodedContent Attempt(string token) => new(new Dictionary<string, string>
    {
        ["Email"] = "admin@mcd.com",
        ["RecoveryKey"] = "not-the-key",
        ["NewPassword"] = "Str0ng-Passw0rd!x",
        ["ConfirmPassword"] = "Str0ng-Passw0rd!x",
        ["__RequestVerificationToken"] = token,
    });

    [Fact]
    public async Task RecoveryKeyAttempts_AreLimitedAcrossAllClients_WithoutAffectingLogin()
    {
        var client = _app.NewClient();
        var token = await AppFactory.GetFormTokenAsync(client, "/admin/account/recover");

        // the first 10 attempts are answered normally (the key is wrong, the form is shown again with an error)
        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/admin/account/recover", Attempt(token))).StatusCode);

        // the 11th is refused, and so is the same attempt from a completely different client (a fresh cookie jar,
        // i.e. what an attacker rotating addresses looks like) — the budget is not per client
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/admin/account/recover", Attempt(token))).StatusCode);
        var other = _app.NewClient();
        var otherToken = await AppFactory.GetFormTokenAsync(other, "/admin/account/recover");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await other.PostAsync("/admin/account/recover", Attempt(otherToken))).StatusCode);

        // viewing the page and signing in are not part of that budget
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/admin/account/recover")).StatusCode);
        var loginToken = await AppFactory.GetFormTokenAsync(other, "/adminlogin");
        var login = await other.PostAsync("/adminlogin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "nobody@example.com", ["Password"] = "wrong-password", ["__RequestVerificationToken"] = loginToken,
        }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
