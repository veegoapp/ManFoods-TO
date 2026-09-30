using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Projected-vs-actual maths behind the Workforce Planning page.</summary>
public class WorkforcePlanningServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WorkforcePlanningService NewService(AppDbContext db) =>
        new(db, new StoreAccessService(db), new MemoryCache(new MemoryCacheOptions()));

    private static void Proj(AppDbContext db, int month, string store, string job, int n) =>
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = month, StoreName = store, JobTitle = job, ProjectedHeadcount = n });

    private static void Active(AppDbContext db, int month, string store, string job, int count)
    {
        for (int i = 0; i < count; i++)
            db.ActiveEmployees.Add(new ActiveEmployee { Year = 2026, Month = month, Store = store, JobTitle = job, EmployeeId = Guid.NewGuid().ToString() });
    }

    [Fact]
    public async Task NoProjection_ReportsNoData()
    {
        var dto = await NewService(NewDb()).GetAsync(null, null, null, null, "Admin", null);
        Assert.False(dto.HasData);
    }

    [Fact]
    public async Task GapAndFillRate_AreProjectedMinusActual()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "GEM", 4);
        Proj(db, 1, "2 | B", "Crew", 10);
        Active(db, 1, "1 | A", "Crew", 8); Active(db, 1, "1 | A", "GEM", 4);
        Active(db, 1, "2 | B", "Crew", 10);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.True(dto.HasActual);
        Assert.Equal(24, dto.Kpis.Projected);
        Assert.Equal(22, dto.Kpis.Actual);
        Assert.Equal(2, dto.Kpis.Gap);
        Assert.Equal(91.7, dto.Kpis.FillPercent);
        var crew = Assert.Single(dto.ByJob, r => r.Name == "Crew");
        Assert.Equal(20, crew.Projected); Assert.Equal(18, crew.Actual); Assert.Equal(2, crew.Gap);
        var a = Assert.Single(dto.ByStore, r => r.Name == "1 | A");
        Assert.Equal("watch", a.Status); // 12 / 14 = 85.7%
        Assert.Equal("ok", Assert.Single(dto.ByStore, r => r.Name == "2 | B").Status);
    }

    [Fact]
    public async Task StoreWithoutProjection_IsNotCountedAsSurplus()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 5);
        Active(db, 1, "1 | A", "Crew", 5);
        Active(db, 1, "9 | Unplanned", "Crew", 7);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(5, dto.Kpis.Actual);
        Assert.DoesNotContain(dto.ByStore, r => r.Name == "9 | Unplanned");
    }

    [Fact]
    public async Task FutureMonthWithoutRoster_ShowsProjectionOnly()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 5); Proj(db, 2, "1 | A", "Crew", 6);
        Active(db, 1, "1 | A", "Crew", 5);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 2, null, null, "Admin", null);

        Assert.False(dto.HasActual);
        Assert.Equal(6, dto.Kpis.Projected);
        Assert.All(dto.ByStore, r => Assert.Equal("none", r.Status));
        Assert.Null(dto.Trend.Single(t => t.Month == 2).Actual);
        Assert.Equal(5, dto.Trend.Single(t => t.Month == 1).Actual);
    }

    [Fact]
    public async Task StoreFill_IsStrictAboutThePeriod_AndMapsEachStore()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "2 | B", "Crew", 10);
        Active(db, 1, "1 | A", "Crew", 8); Active(db, 1, "2 | B", "Crew", 10);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var fill = await svc.GetStoreFillAsync(2026, 1, null, "Admin", null);
        Assert.Equal(2, fill.Count);
        var a = Assert.Single(fill, f => f.Store == "1 | A");
        Assert.Equal(10, a.Projected); Assert.Equal(8, a.Actual); Assert.Equal(80, a.FillPercent); Assert.Equal("critical", a.Status);

        // A month or year with no projection must not fall back to another period.
        Assert.Empty(await svc.GetStoreFillAsync(2026, 5, null, "Admin", null));
        Assert.Empty(await svc.GetStoreFillAsync(2025, 1, null, "Admin", null));
    }

    [Fact]
    public async Task StorePlan_ReturnsJobsAndUpcomingMonths_ForOneStore()
    {
        var db = NewDb();
        for (int m = 1; m <= 6; m++) { Proj(db, m, "1 | A", "Crew", 10 + m); Proj(db, m, "2 | B", "Crew", 99); }
        Active(db, 2, "1 | A", "Crew", 9);
        await db.SaveChangesAsync();

        var plan = await NewService(db).GetStorePlanAsync("1 | A", 2026, 2, "Admin", null);

        Assert.True(plan.HasData);
        Assert.Equal(12, plan.Kpis.Projected);
        var crew = Assert.Single(plan.ByJob);
        Assert.Equal(3, crew.Gap);
        Assert.Equal(new[] { 2, 3, 4, 5 }, plan.Upcoming.Select(u => u.Month)); // this month + next three
        Assert.Equal(new[] { 12, 13, 14, 15 }, plan.Upcoming.Select(u => u.Projected)); // only this store

        Assert.False((await NewService(db).GetStorePlanAsync("9 | Nope", 2026, 2, "Admin", null)).HasData);
    }

    [Fact]
    public async Task StoreAndJobFilters_NarrowTheResult()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "GEM", 4); Proj(db, 1, "2 | B", "Crew", 7);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, "1 | A", "crew", "Admin", null);

        Assert.Equal(10, dto.Kpis.Projected);
        Assert.Single(dto.ByStore);
    }
}

public class WorkforcePlanningReportTests
{
    private sealed class FakePlanning : IWorkforcePlanningService
    {
        public List<MvcApp.Models.ViewModels.PlanningDetailRow> Rows { get; set; } = new();
        public Task<MvcApp.Models.ViewModels.WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName) => throw new NotSupportedException();
        public Task<List<MvcApp.Models.ViewModels.PlanningDetailRow>> GetDetailAsync(int year, IReadOnlyCollection<int>? months, string? stores, string? jobs, string role, string? assignedName) => Task.FromResult(Rows);
        public Task<List<MvcApp.Models.ViewModels.StoreFillDto>> GetStoreFillAsync(int year, int month, string? jobs, string role, string? assignedName) => throw new NotSupportedException();
        public Task<MvcApp.Models.ViewModels.StorePlanDto> GetStorePlanAsync(string store, int year, int month, string role, string? assignedName) => throw new NotSupportedException();
        public Task<List<MvcApp.Models.ViewModels.PeriodItem>> GetProjectionPeriodsAsync() => Task.FromResult(new List<MvcApp.Models.ViewModels.PeriodItem>());
        public Task<List<string>> GetProjectionJobsAsync() => Task.FromResult(new List<string>());
    }

    private static ReportService NewReports(IWorkforcePlanningService planning) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, new AccessAreaContext(), planning);

    [Fact]
    public async Task Report_HasSummaryBreakdownsDataAndPivotTables()
    {
        var fake = new FakePlanning
        {
            Rows =
            {
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "Crew", Projected = 10, Actual = 8 },
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "GEM", Projected = 4, Actual = 4 },
                new() { Year = 2026, Month = 2, Store = "1 | A", Job = "Crew", Projected = 11, Actual = null },
            }
        };
        using var wb = await NewReports(fake).BuildWorkforcePlanningReportAsync(2026, null, null, null, "Admin", null);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        // Re-open the saved file: it must be a valid workbook with the expected sheets…
        using var reopened = new ClosedXML.Excel.XLWorkbook(ms);
        var names = reopened.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("Summary", names);
        Assert.Contains("Data", names);
        Assert.Contains(names, n => n.StartsWith("By Store"));
        Assert.Contains(names, n => n.StartsWith("By Job"));
        Assert.Equal(3, names.Count(n => n.StartsWith("Pivot")));
        Assert.Equal(4, reopened.Worksheet("Data").LastRowUsed()!.RowNumber()); // header + 3 rows

        // …and the raw package must really contain pivot table parts.
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms);
        Assert.Equal(3, zip.Entries.Count(e => e.FullName.StartsWith("xl/pivotTables/pivotTable")));
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("pivotCache/pivotCacheDefinition1.xml"));
    }

    [Fact]
    public async Task Report_WithNoRows_IsJustASummaryNote()
    {
        using var wb = await NewReports(new FakePlanning()).BuildWorkforcePlanningReportAsync(2026, null, null, null, "Admin", null);
        Assert.Single(wb.Worksheets);
    }
}

public class StoreAccessCacheTests
{
    [Fact]
    public async Task OwnStores_AreCached_UntilInvalidated()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.StoreReferences.Add(new StoreReference { Month = 1, Year = 2026, StoreName = "1 | A", OperationManagerEmail = "om@x.com" });
        await db.SaveChangesAsync();
        var access = new StoreAccessService(db, null, null, new MemoryCache(new MemoryCacheOptions()));

        var first = await access.GetOwnStoreNamesAsync("Operation_Manager", "OM@x.com");
        Assert.Equal(new[] { "1 | A" }, first);

        // The database changes, but the cached answer is served until invalidated.
        db.StoreReferences.Add(new StoreReference { Month = 1, Year = 2026, StoreName = "2 | B", OperationManagerEmail = "om@x.com" });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "1 | A" }, await access.GetOwnStoreNamesAsync("Operation_Manager", "om@x.com"));

        StoreAccessService.InvalidateCache();
        Assert.Equal(new[] { "1 | A", "2 | B" }, (await access.GetOwnStoreNamesAsync("Operation_Manager", "om@x.com"))!.OrderBy(s => s));
    }
}
