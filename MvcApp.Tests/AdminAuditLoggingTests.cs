using System.Net;
using System.Text.Json;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Sensitive Admin actions (account and credential changes) leave an "AdminAudit" line in the application
/// log — who did what to which user id — and never a password, OTP, recovery key or the target's email.</summary>
public class AdminAuditLoggingTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public AdminAuditLoggingTests(AppFactory app) => _app = app;

    private string[] AuditLines() => _app.Logs.Lines.Where(l => l.Contains("AdminAudit")).ToArray();

    private async Task<HttpResponseMessage> PostAsync(string path, Dictionary<string, string>? form = null)
    {
        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users");
        form ??= new();
        form["__RequestVerificationToken"] = token;
        return await admin.PostAsync(path, new FormUrlEncodedContent(form));
    }

    // otp.generate is not exercised here: OtpService.GenerateSingleOtpAsync uses ExecuteDelete, which the in-memory
    // test database does not support (the audit line sits after it, in the same controller pattern as the others).
    [Fact]
    public async Task UserLifecycle_IsAudited_WithoutSecretsOrTheTargetEmail()
    {
        var email = "audit-target@example.com";
        var create = await PostAsync("/admin/dashboard/createuser", new() { ["Email"] = email, ["Phone"] = "+201099988877", ["Role"] = "User" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = await _app.QueryAsync(async db => (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Users, u => u.Email == email)).Id);
        Assert.Contains(AuditLines(), l => l.Contains("user.create") && l.Contains($"new user id {id}") && l.Contains("role User") && l.Contains(AppFactory.AdminEmail));

        var edit = await PostAsync($"/admin/dashboard/edituser/{id}", new() { ["Email"] = email, ["Phone"] = "+201099988877", ["Role"] = "Operation_Manager" });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        Assert.Contains(AuditLines(), l => l.Contains("user.update") && l.Contains($"user id {id}") && l.Contains("role now Operation_Manager"));

        var pwdResp = await PostAsync("/admin/dashboard/generatedefaultpassword", new() { ["id"] = id.ToString() });
        var pwdBody = await pwdResp.Content.ReadAsStringAsync();
        Assert.True(pwdResp.StatusCode == HttpStatusCode.OK, $"{(int)pwdResp.StatusCode}: {pwdBody[..Math.Min(200, pwdBody.Length)]}");
        var pwd = JsonDocument.Parse(pwdBody).RootElement.GetProperty("password").GetString()!;
        Assert.Contains(AuditLines(), l => l.Contains("password.generate") && l.Contains($"user id {id}"));

        var delete = await PostAsync("/admin/dashboard/deleteuser", new() { ["id"] = id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Contains(AuditLines(), l => l.Contains("user.delete") && l.Contains($"user id {id}"));

        var everything = string.Join("\n", _app.Logs.Lines);
        Assert.DoesNotContain(pwd, everything);
        Assert.DoesNotContain(email, string.Join("\n", AuditLines()));
    }

    [Fact]
    public async Task DeniedSensitiveAttempts_AreLoggedAsWarnings()
    {
        var other = await _app.AddUserAsync("audit-other-admin@example.com", "Admin");
        var adminOtp = await PostAsync("/admin/dashboard/generateadminotp", new() { ["id"] = other.Id.ToString() });
        Assert.Equal(HttpStatusCode.OK, adminOtp.StatusCode);
        var recovery = await PostAsync("/admin/dashboard/regeneraterecoverykey", new() { ["password"] = "Whatever-123!" });
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);

        var lines = AuditLines();
        Assert.Contains(lines, l => l.StartsWith("Warning") && l.Contains("otp.generate-admin") && l.Contains("DENIED"));
        Assert.Contains(lines, l => l.StartsWith("Warning") && l.Contains("recovery-key.regenerate") && l.Contains("DENIED"));
        Assert.DoesNotContain("Whatever-123!", string.Join("\n", _app.Logs.Lines));
    }
}
