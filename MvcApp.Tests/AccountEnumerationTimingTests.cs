using System.Diagnostics;
using System.Net;
using MvcApp.Models;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Audit: an unknown account must not be distinguishable from a real one by response time
/// (a real account runs a ~100+ ms BCrypt check; an unknown one used to skip it and answer in ~3 ms).
/// Real accounts here use the default work factor, like production.</summary>
public class LoginTimingTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public LoginTimingTests(AppFactory app) => _app = app;

    private async Task<(double Ms, string Body)> LoginAsync(string email)
    {
        var client = _app.NewClient();
        var token = await AppFactory.GetFormTokenAsync(client, "/login");
        var sw = Stopwatch.StartNew();
        var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = "Wrong-Password-999!", ["__RequestVerificationToken"] = token,
        }));
        var ms = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // login page re-rendered with an error
        return (ms, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownAndNoPasswordAccounts_TakeAboutAsLongAsAWrongPasswordForARealOne_AndGetTheSameMessage()
    {
        await _app.AddUserAsync("timing-real@example.com", "User", passwordHashOverride: BCrypt.Net.BCrypt.HashPassword("Right-Password-1!"));
        await _app.AddUserAsync("timing-pending@example.com", "User", passwordHashOverride: null); // bulk-created, no password yet
        await LoginAsync("warmup@example.com"); // JIT / first-use costs

        var real = new List<double>(); var unknown = new List<double>(); var pending = new List<double>();
        string realBody = "", unknownBody = "", pendingBody = "";
        for (var i = 0; i < 2; i++)
        {
            var r = await LoginAsync("timing-real@example.com"); real.Add(r.Ms); realBody = r.Body;
            var u = await LoginAsync($"ghost{i}@example.com"); unknown.Add(u.Ms); unknownBody = u.Body;
            var p = await LoginAsync("timing-pending@example.com"); pending.Add(p.Ms); pendingBody = p.Body;
        }

        var realMs = real.Min();
        Assert.True(unknown.Min() >= realMs * 0.5, $"unknown {unknown.Min():F0} ms vs real {realMs:F0} ms");
        Assert.True(pending.Min() >= realMs * 0.5, $"password-less {pending.Min():F0} ms vs real {realMs:F0} ms");

        // same visible message for all three
        Assert.Contains("Invalid email or password", realBody);
        Assert.Contains("Invalid email or password", unknownBody);
        Assert.Contains("Invalid email or password", pendingBody);
    }
}

/// <summary>Same property for the "forgot password" (OTP) form: an unknown account / no active OTP must cost
/// about as much as a real account with a wrong code. The messages are deliberately unchanged.</summary>
public class OtpTimingTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public OtpTimingTests(AppFactory app) => _app = app;

    private async Task<(double Ms, string Body)> ForgotAsync(string identifier)
    {
        var client = _app.NewClient();
        var token = await AppFactory.GetFormTokenAsync(client, "/home/account/forgotpassword");
        var sw = Stopwatch.StartNew();
        var response = await client.PostAsync("/home/account/forgotpassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Identifier"] = identifier, ["OtpCode"] = "000000", ["NewPassword"] = "New-Password-123!", ["ConfirmPassword"] = "New-Password-123!",
            ["__RequestVerificationToken"] = token,
        }));
        var ms = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (ms, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownAccount_TakesAboutAsLongAsARealOneWithAWrongCode_AndMessagesAreUnchanged()
    {
        var user = await _app.AddUserAsync("otp-real@example.com", "User");
        await _app.SeedAsync(db =>
        {
            db.PasswordResetOtps.Add(new PasswordResetOtp { UserId = user.Id, OtpCode = BCrypt.Net.BCrypt.HashPassword("123456"), ExpiresAt = DateTime.UtcNow.AddHours(1) });
            return Task.CompletedTask;
        });
        await ForgotAsync("warmup-ghost@example.com");

        var real = new List<double>(); var unknown = new List<double>();
        string realBody = "", unknownBody = "";
        for (var i = 0; i < 3; i++) // a real OTP is burned after 5 wrong codes, so stay below that
        {
            var r = await ForgotAsync("otp-real@example.com"); real.Add(r.Ms); realBody = r.Body;
            var u = await ForgotAsync($"otp-ghost{i}@example.com"); unknown.Add(u.Ms); unknownBody = u.Body;
        }

        Assert.True(unknown.Min() >= real.Min() * 0.5, $"unknown {unknown.Min():F0} ms vs real {real.Min():F0} ms");
        Assert.Contains("Incorrect code", realBody);       // unchanged: the real user still sees the attempts left
        Assert.Contains("No active OTP", unknownBody);     // unchanged generic message
    }
}
