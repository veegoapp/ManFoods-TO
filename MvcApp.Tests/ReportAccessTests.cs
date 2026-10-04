using MvcApp.Models.ViewModels;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>A report follows its page: it is hidden from a role the page is hidden from (Settings → Pages), and it is
/// exported under the page's own access area (Settings → Access), so it never shows more stores than the page does.</summary>
public class ReportAccessTests
{
    private sealed class FakePolicy : IAccessPolicyService
    {
        public HashSet<string> Restricted { get; } = new();
        public Task<bool> IsRestrictedAsync(string areaKey) => Task.FromResult(Restricted.Contains(areaKey));
        public Task<bool> AnyOpenAsync() => Task.FromResult(true);
        public Task<Dictionary<string, bool>> GetAllAsync() => Task.FromResult(new Dictionary<string, bool>());
        public Task<List<string>> SaveAsync(Dictionary<string, bool> settings, string? adminName) => Task.FromResult(new List<string>());
    }

    [Fact]
    public void EveryReport_IsTiedToKnownPagesAndAreas()
    {
        foreach (var report in ReportCatalog.All)
        {
            Assert.NotEmpty(report.PageKeys);
            Assert.NotEmpty(report.Areas);
            Assert.All(report.PageKeys, key => Assert.True(UserPages.IsKnown(key), $"{report.Id}: unknown page '{key}'"));
            Assert.All(report.Areas, area => Assert.Contains(area, AccessAreas.All));
        }
    }

    [Fact]
    public void Report_IsHiddenWithItsPage_AndAMultiPageReportNeedsOnlyOneVisiblePage()
    {
        var hiring = ReportCatalog.Find("hiring-forecast")!;
        Assert.True(hiring.IsVisible(new HashSet<string>()));
        Assert.False(hiring.IsVisible(new HashSet<string> { "hiringforecast" }));
        Assert.True(hiring.IsVisible(new HashSet<string> { "workforceplanning", "crewtrainers" })); // other pages do not matter

        var ocOm = ReportCatalog.Find("oc-om-comparison")!;                                      // built on the Workforce and Turnover pages
        Assert.True(ocOm.IsVisible(new HashSet<string> { "workforce" }));
        Assert.False(ocOm.IsVisible(new HashSet<string> { "workforce", "turnover" }));
    }

    [Fact]
    public async Task Export_RunsUnderTheFirstRestrictedAreaOfItsPage_OrNullWhenNoneIsRestricted()
    {
        var policy = new FakePolicy();
        var access = new ReportAccessService(policy);

        Assert.Null(await access.ResolveRestrictedAreaAsync(ReportCatalog.Find("workforce-planning")!));   // nothing restricted: Reports decides

        policy.Restricted.Add(AccessAreas.WorkforcePlanning);
        Assert.Equal(AccessAreas.WorkforcePlanning, await access.ResolveRestrictedAreaAsync(ReportCatalog.Find("workforce-planning")!));
        Assert.Null(await access.ResolveRestrictedAreaAsync(ReportCatalog.Find("turnover")!));              // another page's area does not matter

        policy.Restricted.Add(AccessAreas.ActionCenter);                                                    // Stores Overview draws on three areas
        Assert.Equal(AccessAreas.ActionCenter, await access.ResolveRestrictedAreaAsync(ReportCatalog.Find("stores-overview")!));
    }
}
