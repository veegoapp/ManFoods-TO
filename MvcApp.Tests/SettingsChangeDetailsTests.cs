using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The Activity Log says what an Access / Page Visibility save actually changed, not just how many rows were sent.</summary>
public class SettingsChangeDetailsTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public SettingsChangeDetailsTests(AppFactory app) => _app = app;

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task AccessPolicy_ReturnsOnlyTheAreasThatChanged()
    {
        var policy = new AccessPolicyService(NewDb(), new MemoryCache(new MemoryCacheOptions()));
        var all = AccessAreas.All.ToDictionary(a => a, _ => true);

        Assert.Empty(await policy.SaveAsync(all, "admin"));                                       // same as the default: nothing changed

        all[AccessAreas.CrewTrainers] = false;
        var changes = await policy.SaveAsync(all, "admin");
        Assert.Equal(new[] { "crew_trainers: own stores → all stores" }, changes);

        all[AccessAreas.CrewTrainers] = true;
        Assert.Equal(new[] { "crew_trainers: all stores → own stores" }, await policy.SaveAsync(all, "admin"));
        Assert.Empty(await policy.SaveAsync(all, "admin"));
    }

    [Fact]
    public async Task PageVisibility_ReturnsOnlyThePagesAndRolesThatChanged()
    {
        var svc = new PageVisibilityService(NewDb(), new MemoryCache(new MemoryCacheOptions()));
        var hide = new Dictionary<string, Dictionary<string, bool>> { ["hiringforecast"] = new() { ["User"] = true } };

        Assert.Equal(new[] { "hiringforecast / User: visible → hidden" }, await svc.SaveAsync(hide, "admin"));
        Assert.Empty(await svc.SaveAsync(hide, "admin"));                                          // saving the same thing again changes nothing

        hide["hiringforecast"]["User"] = false;
        Assert.Equal(new[] { "hiringforecast / User: hidden → visible" }, await svc.SaveAsync(hide, "admin"));
    }

    [Fact]
    public async Task TheActivityLogRow_CarriesTheDetails()
    {
        var admin = await _app.AdminClientAsync();
        await ActivityTestHelpers.ResetAsync(_app);

        var open = await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/settings/access-policy", new { settings = new Dictionary<string, bool> { ["crew_trainers"] = false } }, "/admin/dashboard/settings");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var same = await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/settings/access-policy", new { settings = new Dictionary<string, bool> { ["crew_trainers"] = false } }, "/admin/dashboard/settings");
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        var close = await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/settings/access-policy", new { settings = new Dictionary<string, bool> { ["crew_trainers"] = true } }, "/admin/dashboard/settings");
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        var details = await _app.QueryAsync(db => db.ActivityLogs.AsNoTracking().Where(a => a.Action == ActivityActions.SettingsAccessPolicy).OrderBy(a => a.Id).Select(a => a.Details).ToListAsync());
        Assert.Equal(new[] { "crew_trainers: own stores → all stores", "no changes", "crew_trainers: all stores → own stores" }, details);

        var hide = await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/settings/page-visibility",
            new { hidden = new Dictionary<string, Dictionary<string, bool>> { ["crewtrainers"] = new() { ["User"] = true } } }, "/admin/dashboard/settings");
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        var show = await ActivityTestHelpers.PostJsonAsync(_app, admin, "/api/settings/page-visibility",
            new { hidden = new Dictionary<string, Dictionary<string, bool>> { ["crewtrainers"] = new() { ["User"] = false } } }, "/admin/dashboard/settings");
        Assert.Equal(HttpStatusCode.OK, show.StatusCode);

        var pages = await _app.QueryAsync(db => db.ActivityLogs.AsNoTracking().Where(a => a.Action == ActivityActions.SettingsPageVisibility).OrderBy(a => a.Id).Select(a => a.Details).ToListAsync());
        Assert.Equal(new[] { "crewtrainers / User: visible → hidden", "crewtrainers / User: hidden → visible" }, pages);
    }
}
