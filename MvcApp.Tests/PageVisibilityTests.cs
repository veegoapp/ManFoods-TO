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

    [Fact]
    public async Task EverythingIsVisible_UntilAPageIsHidden()
    {
        var svc = NewService();
        Assert.Empty(await svc.GetHiddenAsync());

        await svc.SaveAsync(new Dictionary<string, bool> { ["hiringforecast"] = true, ["crewtrainers"] = true }, "admin");

        var hidden = await svc.GetHiddenAsync();
        Assert.Equal(new[] { "crewtrainers", "hiringforecast" }, hidden.OrderBy(h => h).ToArray());
        Assert.True((await svc.GetAllAsync())["hiringforecast"]);
        Assert.False((await svc.GetAllAsync())["workforce"]);
    }

    [Fact]
    public async Task ShowingAPageAgain_RemovesItFromTheHiddenList()
    {
        var svc = NewService();
        await svc.SaveAsync(new Dictionary<string, bool> { ["stores"] = true }, "admin");
        await svc.SaveAsync(new Dictionary<string, bool> { ["stores"] = false }, "admin");
        Assert.Empty(await svc.GetHiddenAsync());
    }

    [Fact]
    public async Task AtLeastOnePage_MustStayVisible_AndNothingIsSavedOtherwise()
    {
        var svc = NewService();
        var everything = UserPages.All.ToDictionary(p => p.Key, _ => true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(everything, "admin"));

        Assert.Empty(await svc.GetHiddenAsync());
        everything["workforce"] = false;                       // one visible page is enough
        await svc.SaveAsync(everything, "admin");
        Assert.DoesNotContain("workforce", await svc.GetHiddenAsync());
    }

    [Fact]
    public async Task UnknownPageKeys_AreIgnored()
    {
        var svc = NewService();
        await svc.SaveAsync(new Dictionary<string, bool> { ["not-a-page"] = true, ["scorecard"] = true }, "admin");
        Assert.Equal(new[] { "scorecard" }, (await svc.GetHiddenAsync()).ToArray());
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
