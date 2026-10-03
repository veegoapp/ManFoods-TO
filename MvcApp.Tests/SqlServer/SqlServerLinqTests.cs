using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests.SqlServer;

public sealed class SqlServerLinqFixture : SqlServerDatabaseFixture
{
    protected override Task SeedAsync(AppDbContext db) => SqlServerSeedData.SeedAsync(db);
}

/// <summary>
/// The dashboard queries that the in-memory test database cannot vouch for: do they TRANSLATE to SQL Server and return the
/// right rows? Every expectation is computed from the seed data itself (an oracle independent of the database provider),
/// and the larger results are also compared with the same service running on InMemory. Labels are ASCII and lists are
/// sorted ordinally before comparing, so nothing depends on SQL Server's collation or on its row/tie ordering.
/// </summary>
[Trait("Category", "SqlServer")]
public class SqlServerLinqTests : IClassFixture<SqlServerLinqFixture>
{
    private const string Role = "Admin";          // unrestricted: no store filtering, so the queries under test are the only variable
    private const string Email = "admin@example.com";
    private const int Year = SqlServerSeedData.Year;

    private readonly SqlServerLinqFixture _fx;
    public SqlServerLinqTests(SqlServerLinqFixture fx) => _fx = fx;

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DashboardService NewService(AppDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), new StoreAccessService(db), new KeyLocalizer(), new FilterResultCache());

    private sealed class Pair : IAsyncDisposable
    {
        public required AppDbContext SqlDb { get; init; }
        public required AppDbContext MemDb { get; init; }
        public required DashboardService Sql { get; init; }
        public required DashboardService Mem { get; init; }
        public async ValueTask DisposeAsync() { await SqlDb.DisposeAsync(); await MemDb.DisposeAsync(); }
    }

    private async Task<Pair> NewPairAsync()
    {
        var sqlDb = _fx.NewContext(); // the fixture already seeded the SQL Server database
        var memDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await SqlServerSeedData.SeedAsync(memDb);
        return new Pair { SqlDb = sqlDb, MemDb = memDb, Sql = NewService(sqlDb), Mem = NewService(memDb) };
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    private static List<(string Label, int Value)> Sorted(IEnumerable<ChartDataItem> items) =>
        items.Select(i => (i.Label, i.Value)).OrderBy(i => i.Label, StringComparer.Ordinal).ToList();

    private static List<(string Label, int Value)> Sorted(IEnumerable<KeyValuePair<string, int>> items) =>
        items.Select(i => (i.Key, i.Value)).OrderBy(i => i.Key, StringComparer.Ordinal).ToList();

    private static SqlServerSeedData Oracle() => SqlServerSeedData.Build();

    // ── raw EF translation: the building blocks the optimisations rely on ────

    [SqlServerFact]
    public async Task GroupBy_YearMonth_CountsPerPeriod()
    {
        await using var db = _fx.NewContext();
        var rows = await db.ActiveEmployees
            .GroupBy(e => new { e.Year, e.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync();

        var expected = Oracle().Active.GroupBy(e => (e.Year, e.Month)).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(expected.Count, rows.Count);
        Assert.All(rows, r => Assert.Equal(expected[(r.Year, r.Month)], r.Count));
    }

    [SqlServerFact]
    public async Task Contains_OnYearTimes100PlusMonth_FiltersToTheListedPeriods()
    {
        await using var db = _fx.NewContext();
        var keys = new List<int> { 202601, 202603 };

        var active = await db.ActiveEmployees.Where(e => keys.Contains(e.Year * 100 + e.Month)).CountAsync();
        var resigned = await db.Resignations.Where(r => keys.Contains(r.Year * 100 + r.Month)).CountAsync();

        Assert.Equal(Oracle().Active.Count(e => keys.Contains(e.Year * 100 + e.Month)), active);
        Assert.Equal(Oracle().Resignations.Count(r => keys.Contains(r.Year * 100 + r.Month)), resigned);
        Assert.NotEqual(0, active);
    }

    [SqlServerFact]
    public async Task DateOnlyYearAndMonth_TranslateInTheNewHiresPredicate()
    {
        await using var db = _fx.NewContext();
        var keys = new List<int> { 202601, 202602, 202603 };

        // Same shape as DashboardService.NewHiresQuery: hired in the very month of the snapshot row.
        var count = await db.ActiveEmployees
            .Where(e => e.HireDate != null
                        && keys.Contains(e.Year * 100 + e.Month)
                        && (e.HireDate!.Value.Year * 100 + e.HireDate!.Value.Month) == (e.Year * 100 + e.Month))
            .CountAsync();

        var expected = Oracle().Active.Count(e => e.HireDate != null && e.HireDate.Value.Year == e.Year && e.HireDate.Value.Month == e.Month);
        Assert.Equal(expected, count);
        Assert.Equal(4, count);
    }

    [SqlServerFact]
    public async Task Distinct_OnAnonymousMonthYear_ThenOrdered()
    {
        await using var db = _fx.NewContext();
        var rows = await db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Month));
        Assert.All(rows, r => Assert.Equal(Year, r.Year));
    }

    // ── DashboardService: KPIs (GroupBy Year/Month + Contains on the period key) ──

    [SqlServerFact]
    public async Task Kpis_ForARange_MatchTheSeedAndInMemory()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();
        var months = SqlServerSeedData.Months;

        var kpi = await p.Sql.GetKpisAsync(3, Year, null, Role, Email, 1, Year);

        var perPeriod = months.Select(m => seed.Active.Count(e => e.Month == m)).ToList();
        var resignations = seed.Resignations.Count(r => months.Contains(r.Month));
        var newHires = seed.Active.Count(e => e.HireDate != null && e.HireDate.Value.Year == e.Year && e.HireDate.Value.Month == e.Month);

        Assert.Equal(perPeriod.Sum(), kpi.TotalHeadcount);   // range = sum of the period headcounts
        Assert.Equal(resignations, kpi.TotalResignations);
        Assert.Equal(newHires, kpi.NewHires);
        Assert.Equal(MetricsCalculationService.RatePercent(resignations, perPeriod.Average(), 2), kpi.TurnoverRate);
        Assert.Equal((3, Year), (kpi.Month, kpi.Year));

        Assert.Equal(Json(await p.Mem.GetKpisAsync(3, Year, null, Role, Email, 1, Year)), Json(kpi));
    }

    [SqlServerFact]
    public async Task Kpis_ForADiscreteMonthsList_UseOnlyThoseMonths()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();
        var picked = new[] { 1, 3 };

        var kpi = await p.Sql.GetKpisAsync(3, Year, null, Role, Email, months: "1,3");

        var perPeriod = picked.Select(m => seed.Active.Count(e => e.Month == m)).ToList();
        Assert.Equal(perPeriod.Sum(), kpi.TotalHeadcount);
        Assert.Equal(seed.Resignations.Count(r => picked.Contains(r.Month)), kpi.TotalResignations);
        Assert.Equal(Json(await p.Mem.GetKpisAsync(3, Year, null, Role, Email, months: "1,3")), Json(kpi));
    }

    [SqlServerFact]
    public async Task Kpis_WithAStoreFilter_CountOnlyThatStore()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var kpi = await p.Sql.GetKpisAsync(3, Year, "Store B", Role, Email, 1, Year);

        Assert.Equal(seed.Active.Count(e => e.Store == "Store B"), kpi.TotalHeadcount);
        Assert.Equal(seed.Resignations.Count(r => r.Store == "Store B"), kpi.TotalResignations);
        Assert.Equal(Json(await p.Mem.GetKpisAsync(3, Year, "Store B", Role, Email, 1, Year)), Json(kpi));
    }

    // ── breakdowns summed across periods in ONE query (SumBreakdownAsync) ────

    [SqlServerFact]
    public async Task HeadcountByJobTitle_SumsAcrossPeriods()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var rows = await p.Sql.GetHeadcountByJobTitleAsync(3, Year, null, Role, Email, 1, Year);

        var expected = seed.Active.GroupBy(e => e.JobTitle).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(Sorted(expected), Sorted(rows));
        Assert.Equal(Sorted(await p.Mem.GetHeadcountByJobTitleAsync(3, Year, null, Role, Email, 1, Year)), Sorted(rows));
    }

    [SqlServerFact]
    public async Task GenderBreakdown_SumsAcrossPeriods_AndMatchesInMemory()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var rows = await p.Sql.GetGenderBreakdownAsync(3, Year, null, Role, Email, 1, Year);

        Assert.Equal(seed.Active.Count, rows.Sum(r => r.Value));
        Assert.Equal(Sorted(await p.Mem.GetGenderBreakdownAsync(3, Year, null, Role, Email, 1, Year)), Sorted(rows));
    }

    [SqlServerFact]
    public async Task TurnoverByJobTitle_GroupsTheResignationsOfTheRange()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var rows = await p.Sql.GetTurnoverByJobTitleAsync(3, Year, null, Role, Email, 1, Year);

        var expected = seed.Resignations.GroupBy(r => r.JobTitle).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(Sorted(expected), Sorted(rows));
    }

    // ── tenure buckets: hire dates of every period in one query, bucketed in memory ──

    [SqlServerFact]
    public async Task HeadcountByTenure_Buckets_MatchInMemory_AndCoverEveryEmployeeWithAHireDate()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var rows = await p.Sql.GetHeadcountByTenureAsync(3, Year, null, Role, Email, 1, Year);

        Assert.Equal(seed.Active.Count(e => e.HireDate != null), rows.Sum(r => r.Value)); // employees without a hire date are not bucketed
        Assert.Equal(Sorted(await p.Mem.GetHeadcountByTenureAsync(3, Year, null, Role, Email, 1, Year)), Sorted(rows));
    }

    [SqlServerFact]
    public async Task HeadcountByTenure_WithAnOperationManagerFilter_MatchesInMemory()
    {
        await using var p = await NewPairAsync();

        var sql = await p.Sql.GetHeadcountByTenureAsync(3, Year, null, Role, Email, 1, Year, om: "OM One");
        var mem = await p.Mem.GetHeadcountByTenureAsync(3, Year, null, Role, Email, 1, Year, om: "OM One");

        Assert.Equal(Sorted(mem), Sorted(sql));
        Assert.NotEmpty(sql);
    }

    // ── headcount trend: periods + one grouped query (+ one store-reference lookup for OM/OC filters) ──

    [SqlServerFact]
    public async Task HeadcountTrend_AllStores_OnePointPerPeriod()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var rows = await p.Sql.GetHeadcountTrendAsync(null, Role, Email, null, null, null, null, null);

        var expected = SqlServerSeedData.Months.Select(m => ($"{Year:D4}-{m:D2}", seed.Active.Count(e => e.Month == m))).ToList();
        Assert.Equal(expected, rows.Select(r => (r.Label, r.Value)).ToList());
    }

    [SqlServerFact]
    public async Task HeadcountTrend_WithAStoreFilter_CountsOnlyThatStore()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        var rows = await p.Sql.GetHeadcountTrendAsync("Store B", Role, Email, null, null, null, null, null);

        var expected = SqlServerSeedData.Months.Select(m => ($"{Year:D4}-{m:D2}", seed.Active.Count(e => e.Month == m && e.Store == "Store B"))).ToList();
        Assert.Equal(expected, rows.Select(r => (r.Label, r.Value)).ToList());
    }

    [SqlServerFact]
    public async Task HeadcountTrend_WithOperationManagerAndJobFilters_UsesThePeriodsOwnStoreReferences()
    {
        await using var p = await NewPairAsync();
        var seed = Oracle();

        // OM One manages Store A in every period of the seed.
        var rows = await p.Sql.GetHeadcountTrendAsync(null, Role, Email, "OM One", null, null, null, null, "Crew");

        var expected = SqlServerSeedData.Months
            .Select(m => ($"{Year:D4}-{m:D2}", seed.Active.Count(e => e.Month == m && e.Store == "Store A" && e.JobTitle == "Crew")))
            .ToList();
        Assert.Equal(expected, rows.Select(r => (r.Label, r.Value)).ToList());
        Assert.Equal(rows.Select(r => (r.Label, r.Value)),
            (await p.Mem.GetHeadcountTrendAsync(null, Role, Email, "OM One", null, null, null, null, "Crew")).Select(r => (r.Label, r.Value)));
    }

    [SqlServerFact]
    public async Task HeadcountTrend_WithAnOperationManagerNobodyMatches_IsZeroEverywhere()
    {
        await using var p = await NewPairAsync();

        var rows = await p.Sql.GetHeadcountTrendAsync(null, Role, Email, "Nobody", null, null, null, null);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.Value));
    }

    // ── queries with correlated sub-selects over several tables ──────────────

    [SqlServerFact]
    public async Task TrendMatrix_MatchesInMemory()
    {
        await using var p = await NewPairAsync();

        var sql = await p.Sql.GetTrendMatrixAsync(Role, Email);
        var mem = await p.Mem.GetTrendMatrixAsync(Role, Email);

        Assert.Equal(new[] { "2026-01", "2026-02", "2026-03" }, sql.Periods);
        Assert.Equal(2, sql.Rows.Count);
        Assert.Equal(Json(mem), Json(sql));
    }

    [SqlServerFact]
    public async Task StoreComparison_MatchesInMemory()
    {
        await using var p = await NewPairAsync();

        var sql = (await p.Sql.GetStoreComparisonAsync(3, Year, Role, Email, 1, Year)).OrderBy(r => r.StoreName, StringComparer.Ordinal).ToList();
        var mem = (await p.Mem.GetStoreComparisonAsync(3, Year, Role, Email, 1, Year)).OrderBy(r => r.StoreName, StringComparer.Ordinal).ToList();

        Assert.Equal(2, sql.Count);
        Assert.Equal(Json(mem), Json(sql));
    }

    [SqlServerFact]
    public async Task FilterLists_AndAvailablePeriods_MatchInMemory()
    {
        await using var p = await NewPairAsync();

        Assert.Equal(new[] { "OM One", "OM Two" }, (await p.Sql.GetOperationManagersAsync(3, Year, Role, Email)).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(new[] { "Crew", "Manager", "Trainer" }, (await p.Sql.GetJobTitlesAsync(3, Year, Role, Email)).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(new[] { (3, Year), (2, Year), (1, Year) }, (await p.Sql.GetAvailablePeriodsAsync()).Select(x => (x.Month, x.Year)));
        Assert.Equal(Json(await p.Mem.GetAvailablePeriodsAsync()), Json(await p.Sql.GetAvailablePeriodsAsync()));
    }
}
