using System.Net;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

public class AppSmokeTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public AppSmokeTests(AppFactory app) => _app = app;

    [Fact]
    public async Task LoginPage_Renders_AndAdminCanSignIn()
    {
        await _app.AddUserAsync("smoke-admin@example.com", "Admin");
        var client = await _app.SignInAsync("smoke-admin@example.com", AppFactory.AdminPassword, admin: true);
        var users = await client.GetAsync("/admin/dashboard/users");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
    }
}
