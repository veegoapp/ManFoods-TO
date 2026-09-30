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

    private static void Resigned(AppDbContext db, int month, string store, string job, int count)
    {
        for (int i = 0; i < count; i++)
            db.Resignations.Add(new Resignation { Year = 2026, Month = month, Store = store, JobTitle = job, EmployeeId = Guid.NewGuid().ToString() });
    }

    [Fact]
    public async Task HiringNeed_IsShortagePlusExpectedResignations_NeverBelowZero()
    {
        var db = NewDb();
        // Six roster months; month 6 is the one being planned.
        for (int m = 1; m <= 6; m++) { Active(db, m, "9 | Other", "MDS", 1); Resigned(db, m, "1 | A", "Crew", 1); }
        Proj(db, 6, "1 | A", "Crew", 10); Active(db, 6, "1 | A", "Crew", 8);     // short by 2, loses ~1/month -> need 3
        Proj(db, 6, "2 | B", "Crew", 5);  Active(db, 6, "2 | B", "Crew", 8);     // 3 over, no resignations -> need 0
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 6, null, null, "Admin", null);

        Assert.Equal(6, dto.AttritionMonths);
        Assert.Equal(3, Assert.Single(dto.ByStore, r => r.Name == "1 | A").HiringNeed);
        Assert.Equal(1.0, Assert.Single(dto.ByStore, r => r.Name == "1 | A").ExpectedAttrition);
        Assert.Equal(0, Assert.Single(dto.ByStore, r => r.Name == "2 | B").HiringNeed);
        Assert.Equal(3, dto.Kpis.HiringNeed); // a surplus in one store does not offset another's need
    }

    [Fact]
    public async Task HiringNeed_WithoutResignationHistory_EqualsTheShortage()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Active(db, 1, "1 | A", "Crew", 7);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(3, dto.Kpis.HiringNeed);
        Assert.Equal(0, dto.Kpis.ExpectedAttrition);
    }

    [Fact]
    public async Task HiringNeed_OnlyCountsRecentMonths()
    {
        var db = NewDb();
        for (int m = 1; m <= 8; m++) Active(db, m, "9 | Other", "MDS", 1);
        Resigned(db, 1, "1 | A", "Crew", 12);   // older than the last 6 roster months -> ignored
        Resigned(db, 8, "1 | A", "Crew", 6);    // within months 3..8 -> 1 per month
        Proj(db, 8, "1 | A", "Crew", 10); Active(db, 8, "1 | A", "Crew", 10);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 8, null, null, "Admin", null);

        Assert.Equal(1.0, dto.Kpis.ExpectedAttrition);
        Assert.Equal(1, dto.Kpis.HiringNeed);
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
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "Crew", Projected = 10, Actual = 8, ExpectedAttrition = 1.5, HiringNeed = 3.5 },
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "GEM", Projected = 4, Actual = 4, ExpectedAttrition = 0.5, HiringNeed = 0.5 },
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

        // Hiring need columns: Data sheet, month summary and per-store breakdown.
        var dataWs = reopened.Worksheet("Data");
        Assert.Equal("Hiring Need", dataWs.Cell(1, 11).GetString());
        Assert.Equal(3.5, dataWs.Cell(2, 11).GetDouble());
        Assert.Equal(4, reopened.Worksheet("Summary").Cell(9, 6).GetDouble()); // Jan: 3.5 + 0.5 -> 4
        var byStore = reopened.Worksheets.First(w => w.Name.StartsWith("By Store"));
        Assert.Equal("Hiring need (est.)", byStore.Cell(1, 7).GetString());
        Assert.Equal(4, byStore.Cell(2, 7).GetDouble());

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

public class StoreHealthStaffingPillarTests
{
    private static readonly (double Mean, double Std) Dist = (5.0, 5.0);
    private static Dictionary<string, int> NoRisk() => new();

    private static Dictionary<string, MvcApp.Models.ViewModels.StoreFillDto> Fill(string store, int projected, int actual) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [store] = new() { Store = store, Projected = projected, Actual = actual, FillPercent = Math.Round(actual * 100.0 / projected, 1) }
        };

    [Fact]
    public void NoProjection_ScoresOnAtRiskAlone()
    {
        var withoutPlan = StoreHealthService.WorkforcePillar("A", 40, new() { ["A"] = 10 }, Dist, new());
        Assert.True(withoutPlan.HasData);
        Assert.DoesNotContain("gap", withoutPlan.Evidence.Keys);
    }

    [Fact]
    public void Shortfall_RaisesTheScore_AndBlendsSixtyForty()
    {
        // 40 people, 10 high-risk -> ratio 25% vs mean 5 / std 5 -> z = 4 -> capped sub-score 100.
        var risk = new Dictionary<string, int> { ["A"] = 10 };
        var atRiskOnly = StoreHealthService.WorkforcePillar("A", 40, risk, Dist, new());
        // 30 of 40 projected -> 25% short -> gap sub-score 100 (capped at 25%).
        var blended = StoreHealthService.WorkforcePillar("A", 40, risk, Dist, Fill("A", 40, 30));
        Assert.Equal(100, atRiskOnly.SubScore);
        Assert.Equal(100, blended.SubScore);
        Assert.Equal("10", blended.Evidence["gap"]);
        Assert.Equal("25", blended.Evidence["gapPct"]);

        // No at-risk staff at all: the shortfall alone now moves the score (40% weight).
        var calm = StoreHealthService.WorkforcePillar("A", 40, NoRisk(), Dist, new());
        var calmButShort = StoreHealthService.WorkforcePillar("A", 40, NoRisk(), Dist, Fill("A", 40, 30));
        Assert.True(calmButShort.SubScore > calm.SubScore);
        Assert.Equal(40, calmButShort.SubScore); // 0 * 0.6 + 100 * 0.4
    }

    [Fact]
    public void AtOrAboveProjection_IsNotRisk()
    {
        var calm = StoreHealthService.WorkforcePillar("A", 40, NoRisk(), Dist, new());
        var over = StoreHealthService.WorkforcePillar("A", 40, NoRisk(), Dist, Fill("A", 40, 44));
        Assert.Equal(calm.SubScore, over.SubScore);
        Assert.Equal("0", over.Evidence["gapPct"]);
    }

    [Fact]
    public void SmallStore_WithProjection_IsScoredOnTheShortfallAlone()
    {
        // Below the minimum headcount for a rate the at-risk part is skipped, the gap still counts.
        var pillar = StoreHealthService.WorkforcePillar("A", 2, NoRisk(), Dist, Fill("A", 10, 5));
        Assert.True(pillar.HasData);
        Assert.Equal(100, pillar.SubScore);
    }
}
