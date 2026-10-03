using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Data;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests.SqlServer;

/// <summary>
/// The upload / delete paths that InMemory cannot run (ExecuteDeleteAsync, real transactions), driven through the real
/// application host on a throw-away SQL Server database, plus the cache invalidation each of them is supposed to trigger.
/// Each test starts from empty period tables and waits for the action-plan detection job every upload starts, so nothing
/// is still writing when the next test begins or when the database is dropped.
/// </summary>
[Trait("Category", "SqlServer")]
public class SqlServerUploadTests : IClassFixture<SqlServerHostFixture>, IAsyncLifetime
{
    private const string Uploader = "sql-tests";

    private static readonly string[] ActiveHeaders = { "Employee ID", "Name", "Store", "Job Title", "Grade", "Payroll Group", "Cost Center", "Gender", "Hire Date" };
    private static readonly string[] ResignationHeaders = { "Employee ID", "Name", "Store", "Job Title", "Gender", "Hire Date", "Resignation Date", "Payroll Group", "Cost Center" };
    private static readonly string[] StoreHeaders = { "Store Name", "Store Leader", "Operation Consultant", "Operation Manager", "Operation Manager Email", "Operation Consultant Email" };

    private readonly SqlServerHostFixture _fx;
    public SqlServerUploadTests(SqlServerHostFixture fx) => _fx = fx;

    // ── per-test reset ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (!_fx.Enabled) return;
        await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);
        await _fx.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            await db.ActiveEmployees.ExecuteDeleteAsync();
            await db.Resignations.ExecuteDeleteAsync();
            await db.StoreReferences.ExecuteDeleteAsync();
            await db.UploadLogs.ExecuteDeleteAsync();
        });
        // The rows above were removed behind the services' backs, so drop what they cached about them.
        _fx.Factory.Services.GetRequiredService<IMemoryCache>().Remove(DashboardService.AvailablePeriodsCacheKey);
        DataFreshnessService.InvalidateCache();
        _fx.Factory.Services.GetRequiredService<FilterResultCache>().InvalidateAll();
    }

    public async Task DisposeAsync()
    {
        if (_fx.Enabled) await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);
    }

    // ── file builders ────────────────────────────────────────────────────────

    private static IFormFile ActiveFile(params (string Id, string Store, string Job, string Hire)[] rows) =>
        TestFiles.Form(TestFiles.Workbook(ActiveHeaders,
            rows.Select(r => new[] { r.Id, "Employee " + r.Id, r.Store, r.Job, "G1", "Group1", "CC1", "Male", r.Hire })), "active.xlsx");

    private static IFormFile ResignationFile(params (string Id, string Store, string Job)[] rows) =>
        TestFiles.Form(TestFiles.Workbook(ResignationHeaders,
            rows.Select(r => new[] { r.Id, "Leaver " + r.Id, r.Store, r.Job, "Male", "2024-01-01", "2026-03-20", "Group1", "CC1" })), "resignations.xlsx");

    private static IFormFile StoreFile(params string[] stores) =>
        TestFiles.Form(TestFiles.Workbook(StoreHeaders,
            stores.Select(s => new[] { s, "Lead " + s, "OC " + s, "OM " + s, "om-" + s.Replace(' ', '-').ToLowerInvariant() + "@example.com", "oc-" + s.Replace(' ', '-').ToLowerInvariant() + "@example.com" })), "stores.xlsx");

    private Task<(bool success, string message, Dictionary<string, int> rowCounts, string? warning)> UploadPeriodAsync(
        int month, int year, IFormFile active, IFormFile resignations, IFormFile stores) =>
        _fx.InScopeAsync(sp => sp.GetRequiredService<IUploadService>().UploadPeriodDataAsync(active, resignations, stores, month, year, Uploader));

    /// <summary>Period 03/2026: 2 active employees, 1 resignation, 2 stores.</summary>
    private async Task<(bool Success, string Message)> UploadBaselineAsync()
    {
        var result = await UploadPeriodAsync(3, 2026,
            ActiveFile(("1", "Store A", "Crew", "2025-01-10"), ("2", "Store B", "Manager", "2026-03-05")),
            ResignationFile(("9", "Store A", "Crew")),
            StoreFile("Store A", "Store B"));
        Assert.True(result.success, "baseline upload failed: " + result.message);
        return (result.success, result.message);
    }

    // ── observation helpers ──────────────────────────────────────────────────

    private Task<DataFreshnessPeriod?> FreshnessAsync() =>
        _fx.InScopeAsync(sp => sp.GetRequiredService<IDataFreshnessService>().GetLatestDataPeriodAsync());

    private Task<List<(int Month, int Year)>> PeriodsAsync() =>
        _fx.InScopeAsync(async sp =>
            (await sp.GetRequiredService<IDashboardService>().GetAvailablePeriodsAsync()).Select(p => (p.Month, p.Year)).ToList());

    private Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query) =>
        _fx.InScopeAsync(sp => query(sp.GetRequiredService<AppDbContext>()));

    private string CacheProbe() => _fx.Factory!.Services.GetRequiredService<FilterResultCache>().KeyFor("probe", 1);

    // ── tests ────────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task UploadPeriodData_WritesAllThreeTables_AndInvalidatesTheCaches()
    {
        // Prime the caches with the "no data yet" answers.
        Assert.Null(await FreshnessAsync());
        Assert.Empty(await PeriodsAsync());
        var probeBefore = CacheProbe();

        var (success, message) = await UploadBaselineAsync();

        Assert.True(success, message);
        Assert.Equal(2, await DbAsync(db => db.ActiveEmployees.CountAsync(e => e.Month == 3 && e.Year == 2026)));
        Assert.Equal(1, await DbAsync(db => db.Resignations.CountAsync(r => r.Month == 3 && r.Year == 2026)));
        Assert.Equal(2, await DbAsync(db => db.StoreReferences.CountAsync(s => s.Month == 3 && s.Year == 2026)));
        Assert.Equal(new[] { "active_employees", "resignations", "store_reference" },
            (await DbAsync(db => db.UploadLogs.Select(l => l.FileType).ToListAsync())).OrderBy(t => t, StringComparer.Ordinal));

        // The cached answers must not survive the upload.
        Assert.Equal(new DataFreshnessPeriod(3, 2026), await FreshnessAsync());
        Assert.Equal(new[] { (3, 2026) }, await PeriodsAsync());
        Assert.NotEqual(probeBefore, CacheProbe());
    }

    [SqlServerFact]
    public async Task ReUploadingAPeriod_ReplacesItsRowsAndLogs_InsideOneTransaction()
    {
        await UploadBaselineAsync();
        await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);

        var (success, message, _, _) = await UploadPeriodAsync(3, 2026,
            ActiveFile(("77", "Store A", "Trainer", "2024-02-02")),
            ResignationFile(),
            StoreFile("Store A"));

        Assert.True(success, message);
        Assert.Equal(new[] { "77" }, await DbAsync(db => db.ActiveEmployees.Select(e => e.EmployeeId).ToListAsync()));
        Assert.Equal(0, await DbAsync(db => db.Resignations.CountAsync()));
        Assert.Equal(1, await DbAsync(db => db.StoreReferences.CountAsync()));
        Assert.Equal(3, await DbAsync(db => db.UploadLogs.CountAsync())); // one current set of logs per period, not 3 + 3
    }

    [SqlServerFact]
    public async Task StoreReferenceUpload_ForANewerPeriod_UpdatesTheLatestDataPeriodImmediately()
    {
        await UploadBaselineAsync();
        await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);
        Assert.Equal(new DataFreshnessPeriod(3, 2026), await FreshnessAsync()); // now cached
        var probeBefore = CacheProbe();

        var (success, message, _) = await _fx.InScopeAsync(sp => sp.GetRequiredService<IUploadService>()
            .UpdateSingleFileAsync("store_reference", 4, 2026, StoreFile("Store A", "Store C"), Uploader));

        Assert.True(success, message);
        Assert.Equal(2, await DbAsync(db => db.StoreReferences.CountAsync(s => s.Month == 4 && s.Year == 2026)));
        Assert.Equal(2, await DbAsync(db => db.ActiveEmployees.CountAsync())); // the other period files are untouched
        // The regression this guards: the layout's "latest data period" must pick up the new Store Reference period right away.
        Assert.Equal(new DataFreshnessPeriod(4, 2026), await FreshnessAsync());
        Assert.NotEqual(probeBefore, CacheProbe());
    }

    [SqlServerFact]
    public async Task SingleFileReplace_ChangesOnlyThatFileTypeAndItsLog()
    {
        await UploadBaselineAsync();
        await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);

        var (success, message, _) = await _fx.InScopeAsync(sp => sp.GetRequiredService<IUploadService>()
            .UpdateSingleFileAsync("active_employees", 3, 2026, ActiveFile(("50", "Store B", "Crew", "2026-03-01"), ("51", "Store B", "Crew", "2026-03-02"), ("52", "Store B", "Crew", "2026-03-03")), Uploader));

        Assert.True(success, message);
        Assert.Equal(3, await DbAsync(db => db.ActiveEmployees.CountAsync()));
        Assert.Equal(1, await DbAsync(db => db.Resignations.CountAsync()));      // untouched
        Assert.Equal(2, await DbAsync(db => db.StoreReferences.CountAsync()));   // untouched
        Assert.Equal(1, await DbAsync(db => db.UploadLogs.CountAsync(l => l.FileType == "active_employees")));
        Assert.Equal(3, await DbAsync(db => db.UploadLogs.CountAsync()));
    }

    [SqlServerFact]
    public async Task DeletingAPeriodLog_RemovesTheWholePeriod_AndInvalidatesTheCaches()
    {
        await UploadBaselineAsync();
        await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);
        Assert.Equal(new DataFreshnessPeriod(3, 2026), await FreshnessAsync()); // prime
        Assert.Equal(new[] { (3, 2026) }, await PeriodsAsync());                // prime
        var probeBefore = CacheProbe();
        var logId = await DbAsync(db => db.UploadLogs.Where(l => l.FileType == "active_employees").Select(l => l.Id).SingleAsync());

        await _fx.InScopeAsync(sp => sp.GetRequiredService<IUploadService>().DeleteLogAsync(logId));

        Assert.Equal(0, await DbAsync(db => db.ActiveEmployees.CountAsync()));
        Assert.Equal(0, await DbAsync(db => db.Resignations.CountAsync()));
        Assert.Equal(0, await DbAsync(db => db.StoreReferences.CountAsync()));
        Assert.Equal(0, await DbAsync(db => db.UploadLogs.CountAsync()));
        Assert.Null(await FreshnessAsync());
        Assert.Empty(await PeriodsAsync());
        Assert.NotEqual(probeBefore, CacheProbe());
    }

    [SqlServerFact]
    public async Task ADuplicateStoreFile_IsRejectedBeforeAnythingIsWritten()
    {
        await UploadBaselineAsync();
        await _fx.Factory!.WaitForBackgroundJobsAsync(SqlServerHostFixture.BackgroundJobTimeout);

        await Assert.ThrowsAsync<DuplicateStoreReferenceException>(() => UploadPeriodAsync(3, 2026,
            ActiveFile(("99", "Store A", "Crew", "2026-03-01")),
            ResignationFile(),
            StoreFile("Store A", "Store A")));

        // The existing period is exactly as the baseline upload left it.
        Assert.Equal(new[] { "1", "2" }, (await DbAsync(db => db.ActiveEmployees.Select(e => e.EmployeeId).ToListAsync())).OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(1, await DbAsync(db => db.Resignations.CountAsync()));
        Assert.Equal(2, await DbAsync(db => db.StoreReferences.CountAsync()));
        Assert.Equal(3, await DbAsync(db => db.UploadLogs.CountAsync()));
    }

    // ── ExecuteDeleteAsync + transactions, straight against SQL Server ───────

    [SqlServerFact]
    public async Task ExecuteDelete_InsideATransaction_IsUndoneByRollback_AndKeptByCommit()
    {
        await _fx.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            db.ActiveEmployees.AddRange(
                new MvcApp.Models.ActiveEmployee { EmployeeId = "T1", Name = "T1", Store = "Store A", JobTitle = "Crew", Month = 5, Year = 2026 },
                new MvcApp.Models.ActiveEmployee { EmployeeId = "T2", Name = "T2", Store = "Store A", JobTitle = "Crew", Month = 5, Year = 2026 });
            await db.SaveChangesAsync();

            await using (var rolledBack = await db.Database.BeginTransactionAsync())
            {
                var deleted = await db.ActiveEmployees.Where(e => e.Month == 5 && e.Year == 2026).ExecuteDeleteAsync();
                Assert.Equal(2, deleted);
                await rolledBack.RollbackAsync();
            }
            Assert.Equal(2, await db.ActiveEmployees.CountAsync(e => e.Month == 5 && e.Year == 2026));

            await using (var committed = await db.Database.BeginTransactionAsync())
            {
                await db.ActiveEmployees.Where(e => e.Month == 5 && e.Year == 2026).ExecuteDeleteAsync();
                await committed.CommitAsync();
            }
            Assert.Equal(0, await db.ActiveEmployees.CountAsync(e => e.Month == 5 && e.Year == 2026));
        });
    }
}
