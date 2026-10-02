using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Hiding pages from the User interface: the settings, the guard rails and the page-to-key mapping.</summary>
public class PageVisibilityTests
{
    private static PageVisibilityService NewService(AppDbContext? db = null) =>
        new(db ?? new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            new MemoryCache(new MemoryCacheOptions()));

    private static Dictionary<string, Dictionary<string, bool>> Hide(params (string page, string role, bool hidden)[] settings)
    {
        var result = new Dictionary<string, Dictionary<string, bool>>();
        foreach (var (page, role, hidden) in settings)
        {
            if (!result.TryGetValue(page, out var byRole)) result[page] = byRole = new Dictionary<string, bool>();
            byRole[role] = hidden;
        }
        return result;
    }

    private static Dictionary<string, Dictionary<string, bool>> Everything(bool hidden) =>
        UserPages.All.ToDictionary(p => p.Key, _ => UserPages.Roles.ToDictionary(r => r, _ => hidden));

    [Fact]
    public async Task EverythingIsVisible_UntilAPageIsHidden()
    {
        var svc = NewService();
        foreach (var role in UserPages.Roles) Assert.Empty(await svc.GetHiddenAsync(role));

        await svc.SaveAsync(Hide(("hiringforecast", "User", true), ("crewtrainers", "User", true)), "admin");

        var hidden = await svc.GetHiddenAsync("User");
        Assert.Equal(new[] { "crewtrainers", "hiringforecast" }, hidden.OrderBy(h => h).ToArray());
        Assert.True((await svc.GetAllAsync())["hiringforecast"]["User"]);
        Assert.False((await svc.GetAllAsync())["workforce"]["User"]);
    }

    [Fact]
    public async Task ShowingAPageAgain_RemovesItFromTheHiddenList()
    {
        var svc = NewService();
        await svc.SaveAsync(Hide(("stores", "User", true)), "admin");
        await svc.SaveAsync(Hide(("stores", "User", false)), "admin");
        Assert.Empty(await svc.GetHiddenAsync("User"));
    }

    [Fact]
    public async Task AtLeastOnePage_MustStayVisibleForEveryRole_AndNothingIsSavedOtherwise()
    {
        var svc = NewService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(Everything(hidden: true), "admin"));
        foreach (var role in UserPages.Roles) Assert.Empty(await svc.GetHiddenAsync(role));

        // hiding everything for one role is refused even when the other roles keep pages
        var oneRoleEmptied = UserPages.All.ToDictionary(p => p.Key, _ => new Dictionary<string, bool> { ["Head_Manager"] = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(oneRoleEmptied, "admin"));

        var almost = Everything(hidden: true);
        foreach (var role in UserPages.Roles) almost["workforce"][role] = false; // one visible page per role is enough
        await svc.SaveAsync(almost, "admin");
        Assert.DoesNotContain("workforce", await svc.GetHiddenAsync("Operation_Director"));
    }

    [Fact]
    public async Task UnknownPageKeysAndRoles_AreIgnored()
    {
        var svc = NewService();
        await svc.SaveAsync(Hide(("not-a-page", "User", true), ("scorecard", "User", true), ("scorecard", "Admin", true), ("scorecard", "Nobody", true)), "admin");
        Assert.Equal(new[] { "scorecard" }, (await svc.GetHiddenAsync("User")).ToArray());
        Assert.Empty(await svc.GetHiddenAsync("Admin"));
    }

    [Theory]
    [InlineData("Workforce", "workforce")] [InlineData("WorkforcePlanning", "workforceplanning")] [InlineData("NinetyDayTurnover", "ninetyday")]
    [InlineData("ActionPlanGuide", "actioncenter")] [InlineData("ActionCenterDetail", "actioncenter")] [InlineData("StoreProfile", "stores")]
    [InlineData("ReportDetail", "reports")] [InlineData("hiringforecast", "hiringforecast")]
    public void ActionsMapToThePageKeyThatHidesThem(string action, string key) => Assert.Equal(key, UserPages.KeyOfAction(action));

    [Theory]
    [InlineData("Export")] [InlineData("Index")] [InlineData("ChangePassword")] [InlineData(null)]
    public void OtherActions_AreNeverBlocked(string? action) => Assert.Null(UserPages.KeyOfAction(action));

    [Fact]
    public void FirstVisible_SkipsHiddenPages_InSidebarOrder()
    {
        Assert.Equal("workforce", UserPages.FirstVisible(new HashSet<string>())!.Key);
        Assert.Equal("hiringforecast", UserPages.FirstVisible(new HashSet<string> { "workforce", "workforceplanning" })!.Key);
        Assert.Null(UserPages.FirstVisible(UserPages.All.Select(p => p.Key).ToHashSet()));
    }

    [Fact]
    public void EveryPageKeyIsUniqueAndMapsBackToItsOwnAction()
    {
        Assert.Equal(UserPages.All.Count, UserPages.All.Select(p => p.Key).Distinct().Count());
        foreach (var page in UserPages.All) Assert.Equal(page.Key, UserPages.KeyOfAction(page.Action));
    }
}
