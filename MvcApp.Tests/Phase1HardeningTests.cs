using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Audit phase 1: no internal exception text in the upload redirects, a hardened language cookie,
/// a working /error route, and no full email addresses in the login logs.</summary>
public class Phase1HardeningTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public Phase1HardeningTests(AppFactory app) => _app = app;

    // ── mf-lang cookie ───────────────────────────────────────────────────────

    [Fact]
    public async Task LanguageCookie_IsHttpOnlySecureAndLax_AndStillSwitchesTheLanguage()
    {
        var client = _app.NewClient();
        var token = await AppFactory.GetFormTokenAsync(client, "/login");
        var response = await client.PostAsync("/language/set", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["lang"] = "ar", ["returnUrl"] = "/login", ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("mf-lang=ar"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // the cookie is still read on the next request: the page now renders right-to-left
        var login = await (await client.GetAsync("/login")).Content.ReadAsStringAsync();
        Assert.Contains("dir=\"rtl\"", login);
    }

    [Fact]
    public async Task LanguageCookie_UnsupportedValue_FallsBackToEnglish_AsBefore()
    {
        var client = _app.NewClient();
        var token = await AppFactory.GetFormTokenAsync(client, "/login");
        var response = await client.PostAsync("/language/set", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["lang"] = "xx", ["__RequestVerificationToken"] = token,
        }));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("mf-lang=en"));
    }

    // ── /error route ─────────────────────────────────────────────────────────

    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        // appended after the app's own pipeline, so it only runs for a path no endpoint handles — and is still
        // inside UseExceptionHandler, which is registered first.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((ctx, nxt) => ctx.Request.Path == "/__boom" ? throw new InvalidOperationException("secret-internal-detail") : nxt());
        };
    }

    [Fact]
    public async Task UnhandledException_ShowsTheLocalizedErrorPage_WithoutInternalDetails()
    {
        using var prod = _app.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureServices(s => s.AddTransient<IStartupFilter, ThrowingStartupFilter>());
        });
        var client = prod.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/__boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Request ID:", body);
        Assert.DoesNotContain("secret-internal-detail", body);
    }

    [Fact]
    public async Task ErrorPage_Renders_ForAnyVisitor()
    {
        var response = await _app.NewClient().GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Request ID:", await response.Content.ReadAsStringAsync());
    }

    // ── upload error messages ────────────────────────────────────────────────

    private static MultipartFormDataContent Upload(string token, byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "File", fileName);
        return form;
    }

    [Fact]
    public async Task Upload_UnexpectedFailure_ShowsAGenericMessage_NotTheExceptionText()
    {
        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/uploads");
        // passes the signature/size guard (a zip with the two workbook parts) but is not a readable workbook
        var response = await admin.PostAsync("/admin/dashboard/uploadjobpayrollgroups", Upload(token, TestFiles.ZipWithPayload(0), "groups.xlsx"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Uri.UnescapeDataString(response.Headers.Location!.OriginalString).Replace('+', ' ');
        Assert.Contains("Unexpected error while processing the file", location);
    }

    [Fact]
    public async Task Upload_ValidationMessage_IsStillShownToTheAdmin()
    {
        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/uploads");
        var response = await admin.PostAsync("/admin/dashboard/uploadjobpayrollgroups", Upload(token, System.Text.Encoding.UTF8.GetBytes("MZ not excel"), "groups.xlsx"));

        Assert.Contains("Only Excel files", Uri.UnescapeDataString(response.Headers.Location!.OriginalString).Replace('+', ' '));
    }

    // ── logs ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("jane.doe@example.com", "j***@example.com")]
    [InlineData("  A@B.org ", "a***@b.org")]
    [InlineData("no-at-sign", "***")]
    [InlineData("", "(empty)")]
    [InlineData(null, "(empty)")]
    public void MaskEmail_KeepsOnlyTheFirstLetterAndDomain(string? input, string expected) =>
        Assert.Equal(expected, AuthService.MaskEmail(input));

    private sealed class CapturingProvider : ILoggerProvider, ILogger
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(formatter(state, exception));
        public void Dispose() { }
    }

    [Fact]
    public async Task FailedLogins_AreStillLogged_WithoutTheFullEmailAddress()
    {
        var logs = new CapturingProvider();
        using var withLogs = _app.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        await _app.AddUserAsync("log.subject@example.com", "User");
        var client = withLogs.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var token = await AppFactory.GetFormTokenAsync(client, "/login");

        foreach (var email in new[] { "ghost.person@example.com", "log.subject@example.com" })
            await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email, ["Password"] = "definitely-wrong", ["__RequestVerificationToken"] = token,
            }));

        var all = string.Join("\n", logs.Lines);
        Assert.Contains("Login failed", all);                      // the security log is still there
        Assert.Contains("g***@example.com", all);
        Assert.DoesNotContain("ghost.person", all);
        Assert.DoesNotContain("log.subject", all);
        Assert.DoesNotContain("definitely-wrong", all);
    }
}
