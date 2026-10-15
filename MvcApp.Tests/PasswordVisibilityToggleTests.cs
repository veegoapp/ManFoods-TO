using System.Net;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The show/hide ("eye") button on the Login, Forgot Password and Change Password pages of both portals.</summary>
public class PasswordVisibilityToggleTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public PasswordVisibilityToggleTests(AppFactory app) => _app = app;

    [Theory]
    [InlineData("/adminlogin", false)]
    [InlineData("/login", false)]
    [InlineData("/Admin/Account/ForgotPassword", false)]
    [InlineData("/Home/Account/ForgotPassword", false)]
    [InlineData("/Admin/Account/ChangePassword", true)]
    [InlineData("/Home/Account/ChangePassword", true)]
    public async Task EveryPasswordPage_LoadsTheToggleScript_WithBothLabels(string url, bool signedIn)
    {
        var client = !signedIn ? _app.NewClient()
            : url.StartsWith("/Admin") ? await _app.AdminClientAsync() : await _app.UserClientAsync();

        var res = await client.GetAsync(url);
        var html = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("/js/password-toggle.js", html);
        Assert.Contains("data-show-label=\"Show password\"", html);
        Assert.Contains("data-hide-label=\"Hide password\"", html);
        Assert.Contains("type=\"password\"", html); // the page really has a password field for it to act on
    }

    [Fact]
    public async Task TheToggleScript_IsServed_AndOnlyTouchesPasswordInputs()
    {
        var res = await _app.NewClient().GetAsync("/js/password-toggle.js");
        var js = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("input[type=\"password\"]", js);
        Assert.Contains("bi-eye-slash", js);
    }

    [Fact]
    public async Task ArabicPages_GetArabicButtonLabels()
    {
        var client = _app.NewClient();
        client.DefaultRequestHeaders.Add("Cookie", "mf-lang=ar");

        var html = WebUtility.HtmlDecode(await (await client.GetAsync("/adminlogin")).Content.ReadAsStringAsync()); // Razor writes non-ASCII as entities

        Assert.Contains("data-show-label=\"إظهار كلمة المرور\"", html);
        Assert.Contains("data-hide-label=\"إخفاء كلمة المرور\"", html);
    }
}
