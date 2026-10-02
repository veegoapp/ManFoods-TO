using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>M4 — results keyed by user-typed filters live in a bounded cache under normalized keys.</summary>
public class FilterCacheTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ── key normalization ────────────────────────────────────────────────────

    [Theory]
    [InlineData("A,B", " B ,A,A, ")]
    [InlineData("A,B", "B,A")]
    [InlineData("", null)]
    [InlineData("", "  ")]
    [InlineData("", ",,")]
    [InlineData("Store One", "  Store One ")]
    public void EquivalentFilterSelections_ShareOneKeyPart(string a, string? b) =>
        Assert.Equal(FilterResultCache.NormalizeList(a), FilterResultCache.NormalizeList(b));

    [Fact]
    public void Normalization_KeepsDifferentSelectionsApart_AndCaseIsPreserved()
    {
        Assert.NotEqual(FilterResultCache.NormalizeList("A,B"), FilterResultCache.NormalizeList("A,C"));
        Assert.NotEqual(FilterResultCache.NormalizeList("store a"), FilterResultCache.NormalizeList("Store A")); // some comparisons are case-sensitive
        Assert.Equal("A,B,C", FilterResultCache.NormalizeList("C,A,B"));
    }

    [Theory]
    [InlineData("3,1,1,x,13,0", "1,3")]
    [InlineData(" 12 , 2 ", "2,12")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("zz", "")]
    public void MonthLists_KeepOnlyWhatResolvePeriodsKeeps(string? input, string expected) =>
        Assert.Equal(expected, FilterResultCache.NormalizeMonths(input));

    [Fact]
    public void NormalizedMonths_ResolveToTheSamePeriods_AsTheRawValue()
    {
        const string raw = "5, 1,1,x,13,3";
        Assert.Equal(DashboardService.ResolvePeriods(null, 2026, null, null, raw),
                     DashboardService.ResolvePeriods(null, 2026, null, null, FilterResultCache.NormalizeMonths(raw)));
    }

    [Fact]
    public void Keys_AreFixedLength_NoMatterHowLongTheFilterValuesAre()
    {
        var small = FilterResultCache.BuildKey("kpi", 1, 2026, "A", "x");
        var huge = FilterResultCache.BuildKey("kpi", 1, 2026, new string('A', 100_000), "x");

        Assert.Equal(small.Length, huge.Length);
        Assert.NotEqual(small, huge);
        Assert.Equal(small, FilterResultCache.BuildKey("kpi", 1, 2026, "A", "x"));
        Assert.NotEqual(FilterResultCache.BuildKey("kpi", "ab", "c"), FilterResultCache.BuildKey("kpi", "a", "bc")); // part boundaries matter
    }

    // ── the bounded cache ────────────────────────────────────────────────────

    [Fact]
    public void NormalUse_Works_StoreReadAndExpire()
    {
        var cache = new FilterResultCache();
        var value = new List<int> { 1, 2, 3 };

        Assert.False(cache.TryGet<List<int>>("k", out _));
        cache.Set("k", value, TimeSpan.FromMinutes(5), count: value.Count);

        Assert.True(cache.TryGet<List<int>>("k", out var hit));
        Assert.Same(value, hit);

        cache.Set("short", "v", TimeSpan.FromMilliseconds(30));
        Thread.Sleep(120);
        Assert.False(cache.TryGet<string>("short", out _));
    }

    [Fact]
    public void ArbitraryDistinctKeys_CannotGrowTheCachePastItsLimit()
    {
        var cache = new FilterResultCache(sizeLimit: 100);

        for (var i = 0; i < 20_000; i++)
            cache.Set(FilterResultCache.BuildKey("kpi", "store-" + i), i, TimeSpan.FromMinutes(5));

        Assert.True(cache.Count <= 100, $"cache holds {cache.Count} entries, limit is 100");
        Assert.True(cache.Count > 0);
    }

    [Fact]
    public void LargeResults_CostMore_SoFewerFitThanSmallOnes()
    {
        var cache = new FilterResultCache(sizeLimit: 1000);

        for (var i = 0; i < 500; i++) cache.Set("big" + i, new List<int>(), TimeSpan.FromMinutes(5), count: 5000); // weight 501 each

        Assert.True(cache.Count <= 2, $"expected at most 2 big entries, got {cache.Count}");
    }

    // ── through the real services ────────────────────────────────────────────

    private static async Task<DashboardService> NewDashboardAsync(AppDbContext db, FilterResultCache cache)
    {
        db.ActiveEmployees.Add(new ActiveEmployee { EmployeeId = "1", Name = "A", Store = "Store A", JobTitle = "Crew", Month = 3, Year = 2026 });
        await db.SaveChangesAsync();
        return new DashboardService(db, new MemoryCache(new MemoryCacheOptions()), new StoreAccessService(db), new KeyLocalizer(), cache);
    }

    [Fact]
    public async Task Dashboard_EquivalentFilters_ShareOneCachedEntry_AndStillReturnTheSameResult()
    {
        var cache = new FilterResultCache();
        var svc = await NewDashboardAsync(NewDb(), cache);

        var a = await svc.GetKpisAsync(3, 2026, "Store A,Store B", "Admin", "x@example.com", om: "O1,O2", months: "3,1");
        var b = await svc.GetKpisAsync(3, 2026, " Store B, Store A ", "Admin", "x@example.com", om: "O2, O1", months: "1, 3, 3");

        Assert.Equal(1, cache.Count);
        Assert.Same(a, b); // the second request was served from the cache
    }

    [Fact]
    public async Task Dashboard_DifferentFilters_AreNotMixedUp()
    {
        var cache = new FilterResultCache();
        var svc = await NewDashboardAsync(NewDb(), cache);

        var storeA = await svc.GetKpisAsync(3, 2026, "Store A", "Admin", "x@example.com");
        var storeZ = await svc.GetKpisAsync(3, 2026, "Store Z", "Admin", "x@example.com");

        Assert.Equal(2, cache.Count);
        Assert.NotSame(storeA, storeZ);
        Assert.True(storeA.TotalHeadcount > storeZ.TotalHeadcount); // Store A has the employee, Store Z none
    }

    [Fact]
    public async Task Dashboard_ThousandsOfArbitraryStoreValues_StayWithinTheCacheLimit()
    {
        var cache = new FilterResultCache(sizeLimit: 40);
        var svc = await NewDashboardAsync(NewDb(), cache);

        for (var i = 0; i < 400; i++)
            await svc.GetKpisAsync(3, 2026, "no-such-store-" + i, "Admin", "x@example.com");

        Assert.True(cache.Count <= 40, $"cache holds {cache.Count} entries, limit is 40");
    }

    [Fact]
    public async Task EarlyWarning_ArbitraryFilterValues_StayWithinTheCacheLimit_AndNormalRequestsAreCached()
    {
        var db = NewDb();
        db.ActiveEmployees.Add(new ActiveEmployee { EmployeeId = "1", Name = "A", Store = "Store A", JobTitle = "Crew", Month = 3, Year = 2026, HireDate = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var cache = new FilterResultCache(sizeLimit: 30);
        var svc = new EarlyWarningService(db, new StoreAccessService(db), new MemoryCache(new MemoryCacheOptions()), http, cache);

        for (var i = 0; i < 300; i++)
            await svc.GetWatchlistAsync("junk-" + i, "Admin", "x@example.com", months: i % 12 + ",x", year: 2026);
        Assert.True(cache.Count <= 30, $"cache holds {cache.Count} entries, limit is 30");

        var roomy = new FilterResultCache();
        var normal = new EarlyWarningService(db, new StoreAccessService(db), new MemoryCache(new MemoryCacheOptions()), http, roomy);
        var first = await normal.GetWatchlistAsync("Store A,Store B", "Admin", "x@example.com", months: "3", year: 2026);
        var countAfterFirst = roomy.Count;
        var second = await normal.GetWatchlistAsync("Store B, Store A", "Admin", "x@example.com", months: " 3 ", year: 2026);

        Assert.Same(first, second);
        Assert.Equal(countAfterFirst, roomy.Count);
    }

    [Fact]
    public async Task EarlyWarning_JobsFromTheQueryString_AreNormalizedToo()
    {
        var db = NewDb();
        db.ActiveEmployees.Add(new ActiveEmployee { EmployeeId = "1", Name = "A", Store = "Store A", JobTitle = "Crew", Month = 3, Year = 2026, HireDate = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();
        var context = new DefaultHttpContext();
        var http = new HttpContextAccessor { HttpContext = context };
        var cache = new FilterResultCache();
        var svc = new EarlyWarningService(db, new StoreAccessService(db), new MemoryCache(new MemoryCacheOptions()), http, cache);

        context.Request.QueryString = new QueryString("?jobs=Crew,Cashier");
        var a = await svc.GetWatchlistAsync(null, "Admin", "x@example.com");
        context.Request.QueryString = new QueryString("?jobs=Cashier, Crew,Crew");
        var b = await svc.GetWatchlistAsync(null, "Admin", "x@example.com");

        Assert.Same(a, b);
    }
}
