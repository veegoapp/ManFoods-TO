using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The month-by-month series the Comparisons page draws (one grouped read for the whole range).</summary>
public class MonthlySeriesTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DashboardService Service(AppDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), new StoreAccessService(db), new KeyLocalizer(), new FilterResultCache());

    private static ActiveEmployee Emp(string id, string store, int month, DateOnly? hired = null, string job = "Crew") =>
        new() { EmployeeId = id, Name = id, Store = store, JobTitle = job, Month = month, Year = 2026, HireDate = hired };

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = NewDb();
        db.ActiveEmployees.AddRange(
            Emp("1", "Store A", 1), Emp("2", "Store A", 1),                                              // January: 2
            Emp("1", "Store A", 2), Emp("2", "Store A", 2), Emp("3", "Store B", 2, new DateOnly(2026, 2, 10), "Manager")); // February: 3, one hired in February
        db.Resignations.Add(new Resignation { EmployeeId = "9", Name = "9", Store = "Store A", JobTitle = "Crew", Month = 2, Year = 2026 });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task OnePointPerRosterMonth_WithHeadcountHiresResignationsAndRate()
    {
        var series = await Service(await SeedAsync()).GetMonthlySeriesAsync(null, 2026, null, "Admin", "x@example.com", months: "1,2,3");

        // March has no roster upload, so it is not in the series
        Assert.Equal(new[] { (2026, 1), (2026, 2) }, series.Select(p => (p.Year, p.Month)));
        Assert.Equal(new[] { 2, 3 }, series.Select(p => p.Headcount));
        Assert.Equal(new[] { 0, 1 }, series.Select(p => p.NewHires));
        Assert.Equal(new[] { 0, 1 }, series.Select(p => p.Resignations));
        Assert.Equal(0, series[0].TurnoverRate);
        Assert.Equal(33.33, series[1].TurnoverRate, 2);
    }

    [Fact]
    public async Task StoreAndJobFilters_NarrowEveryMonth()
    {
        var svc = Service(await SeedAsync());

        var storeA = await svc.GetMonthlySeriesAsync(null, 2026, "Store A", "Admin", "x@example.com", months: "1,2");
        Assert.Equal(new[] { 2, 2 }, storeA.Select(p => p.Headcount));
        Assert.Equal(new[] { 0, 0 }, storeA.Select(p => p.NewHires));

        var managers = await svc.GetMonthlySeriesAsync(null, 2026, null, "Admin", "x@example.com", months: "1,2", jobTitles: "Manager");
        Assert.Equal(new[] { 1 }, managers.Select(p => p.Headcount));   // only February has a Manager
        Assert.Equal(new[] { 1 }, managers.Select(p => p.NewHires));
    }

    [Fact]
    public async Task EquivalentRequests_ShareTheCachedSeries()
    {
        var svc = Service(await SeedAsync());
        var a = await svc.GetMonthlySeriesAsync(null, 2026, null, "Admin", "x@example.com", months: "2,1");
        var b = await svc.GetMonthlySeriesAsync(null, 2026, null, "Admin", "x@example.com", months: "1, 2, 2");
        Assert.Same(a, b);
    }
}
