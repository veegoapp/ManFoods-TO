using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Who was hired in a period and whether they are still working — behind the Hires Tracker report.</summary>
public class NewHireStatusServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static NewHireStatusService NewService(AppDbContext db, IMemoryCache? cache = null) =>
        new(db, new StoreAccessService(db), cache ?? new MemoryCache(new MemoryCacheOptions()));

    private static void Roster(AppDbContext db, int year, int month, string id, string store, string hire, string gender = "Male", string payroll = "Hourly") =>
        db.ActiveEmployees.Add(new ActiveEmployee
        {
            Year = year, Month = month, EmployeeId = id, Name = "N" + id, Store = store, JobTitle = "Crew",
            Gender = gender, PayrollGroup = payroll, HireDate = DateOnly.Parse(hire),
        });

    private static void Resigned(AppDbContext db, string id, string store, string hire, string left) =>
        db.Resignations.Add(new Resignation
        {
            Year = 2026, Month = 5, EmployeeId = id, Name = "N" + id, Store = store, JobTitle = "Crew",
            HireDate = DateOnly.Parse(hire), ResignationDate = DateOnly.Parse(left),
        });

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = NewDb();
        // 1: hired Feb 2026, still on the latest (May) roster. 2: hired Mar 2026, resigned after 30 days.
        // 3: hired Jan 2025 (other year). 4: hired Apr 2026, on April roster only and no resignation record.
        Roster(db, 2026, 4, "1", "S1", "2026-02-10");
        Roster(db, 2026, 5, "1", "S1", "2026-02-10", gender: "Female");
        Roster(db, 2026, 4, "2", "S2", "2026-03-01");
        Resigned(db, "2", "S2", "2026-03-01", "2026-03-31");
        Roster(db, 2026, 5, "3", "S1", "2025-01-05");
        Roster(db, 2026, 4, "4", "S1", "2026-04-15");
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task OnlyPeopleHiredInTheYearAreListed_WithTheirStatus()
    {
        var rows = await NewService(await SeedAsync()).GetAsync("Admin", null, 2026, null, null, null);

        Assert.Equal(new[] { "4", "2", "1" }, rows.Select(r => r.EmployeeId));   // newest hire first, 2025 hire left out
        Assert.Equal(NewHireStatuses.Active, rows.Single(r => r.EmployeeId == "1").Status);
        Assert.Equal(NewHireStatuses.NotOnLatestRoster, rows.Single(r => r.EmployeeId == "4").Status);
        var left = rows.Single(r => r.EmployeeId == "2");
        Assert.Equal(NewHireStatuses.Resigned, left.Status);
        Assert.Equal(new DateOnly(2026, 3, 31), left.ResignationDate);
        Assert.Equal(30, left.TenureDays);
        Assert.True(left.LeftWithin90Days);
    }

    [Fact]
    public async Task EmployeeDetailsComeFromTheLatestRosterRow()
    {
        var one = (await NewService(await SeedAsync()).GetAsync("Admin", null, 2026, null, null, null)).Single(r => r.EmployeeId == "1");
        Assert.Equal("Female", one.Gender);   // the May row, not the April one
    }

    [Fact]
    public async Task MonthsStoresAndStatusNarrowTheList()
    {
        var svc = NewService(await SeedAsync());

        Assert.Equal(new[] { "2" }, (await svc.GetAsync("Admin", null, 2026, new[] { 3 }, null, null)).Select(r => r.EmployeeId));
        Assert.Equal(new[] { "2" }, (await svc.GetAsync("Admin", null, 2026, null, new[] { "S2" }, null)).Select(r => r.EmployeeId));
        Assert.Equal(new[] { "1" }, (await svc.GetAsync("Admin", null, 2026, null, null, "Active")).Select(r => r.EmployeeId));
        Assert.Equal(new[] { "2" }, (await svc.GetAsync("Admin", null, 2026, null, null, "resigned")).Select(r => r.EmployeeId));
    }

    [Fact]
    public async Task RecordsAreCachedUntilTheCacheKeyIsCleared()
    {
        var db = await SeedAsync();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = NewService(db, cache);
        Assert.Equal(3, (await svc.GetAsync("Admin", null, 2026, null, null, null)).Count);

        Roster(db, 2026, 5, "5", "S1", "2026-05-02");
        await db.SaveChangesAsync();
        Assert.Equal(3, (await svc.GetAsync("Admin", null, 2026, null, null, null)).Count);   // served from the cache, no extra DB read

        cache.Remove(NewHireStatusService.RecordsCacheKey);
        Assert.Equal(4, (await svc.GetAsync("Admin", null, 2026, null, null, null)).Count);
    }
}
