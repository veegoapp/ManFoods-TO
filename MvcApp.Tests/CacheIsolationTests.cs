using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>
/// A cached result must never be handed to a caller who is not allowed to see it. A restricted role's store
/// scope depends on the endpoint's access area and on the admin's per-area policy (see StoreAccessService),
/// not only on role + email, so the cache keys carry the RESOLVED accessible-store scope. These tests drive the
/// real services with one shared cache and switch the area / policy / user between calls.
/// </summary>
public class CacheIsolationTests
{
    private const string Role = "Operation_Manager";
    private const string OmOne = "om1@example.com";

    private sealed class Env
    {
        public required AppDbContext Db { get; init; }
        public required AccessAreaContext Area { get; init; }
        public required FilterResultCache Cache { get; init; }
        public required AccessPolicyService Policy { get; init; }
        public required StoreAccessService Access { get; init; }
        public required DashboardService Dashboard { get; init; }
        public required StoreService Stores { get; init; }
        public required ExitInterviewService Exit { get; init; }
        public required ScorecardService Scorecard { get; init; }
    }

    /// <summary>Store A belongs to OM One (om1@), Store B to OM Two. <paramref name="openAreas"/> are areas the
    /// admin has opened to every store (IsRestricted = false); every other area stays restricted.</summary>
    private static async Task<Env> NewEnvAsync(params string[] openAreas)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.StoreReferences.AddRange(
            new StoreReference { StoreName = "Store A", Month = 3, Year = 2026, StoreLeader = "Lead A", OperationManager = "OM One", OperationManagerEmail = OmOne },
            new StoreReference { StoreName = "Store B", Month = 3, Year = 2026, StoreLeader = "Lead B", OperationManager = "OM Two", OperationManagerEmail = "om2@example.com" });
        db.ActiveEmployees.AddRange(
            new ActiveEmployee { EmployeeId = "1", Name = "A1", Store = "Store A", JobTitle = "Crew", Month = 3, Year = 2026 },
            new ActiveEmployee { EmployeeId = "2", Name = "A2", Store = "Store A", JobTitle = "Crew", Month = 3, Year = 2026 },
            new ActiveEmployee { EmployeeId = "3", Name = "B1", Store = "Store B", JobTitle = "Crew", Month = 3, Year = 2026 },
            new ActiveEmployee { EmployeeId = "4", Name = "B2", Store = "Store B", JobTitle = "Crew", Month = 3, Year = 2026 },
            new ActiveEmployee { EmployeeId = "5", Name = "B3", Store = "Store B", JobTitle = "Crew", Month = 3, Year = 2026 });
        db.ExitInterviews.AddRange(
            new ExitInterview { FormsResponseId = "r1", EmployeeId = "1", Store = "Store A", ReasonForLeaving = "Pay" },
            new ExitInterview { FormsResponseId = "r2", EmployeeId = "3", Store = "Store B", ReasonForLeaving = "Pay" },
            new ExitInterview { FormsResponseId = "r3", EmployeeId = "4", Store = "Store B", ReasonForLeaving = "Pay" });
        foreach (var area in openAreas)
            db.PageAccessConfigs.Add(new PageAccessConfig { AreaKey = area, IsRestricted = false, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var cache = new FilterResultCache();
        var areaContext = new AccessAreaContext();
        var policy = new AccessPolicyService(db, new MemoryCache(new MemoryCacheOptions()), cache);
        var access = new StoreAccessService(db, policy, areaContext); // no own cache: scope changes show up immediately
        var memory = new MemoryCache(new MemoryCacheOptions());
        var exit = new ExitInterviewService(db, access, new KeyLocalizer(), cache);
        return new Env
        {
            Db = db, Area = areaContext, Cache = cache, Policy = policy, Access = access,
            Dashboard = new DashboardService(db, memory, access, new KeyLocalizer(), cache),
            Stores = new StoreService(db, access, cache),
            Exit = exit,
            Scorecard = new ScorecardService(db, exit, access, memory, new HttpContextAccessor(), cache),
        };
    }

    // ── the scope fingerprint itself ─────────────────────────────────────────

    [Fact]
    public void ScopeKey_IsAnOrderAndCaseInsensitiveSet_AndDistinguishesScopes()
    {
        Assert.Equal("ALL", AccessScopeKey.Of(null));
        Assert.Equal(AccessScopeKey.Of(new[] { "Store A", "Store B" }), AccessScopeKey.Of(new[] { " store b", "STORE A", "Store A" }));
        Assert.NotEqual(AccessScopeKey.Of(new[] { "Store A" }), AccessScopeKey.Of(new[] { "Store A", "Store B" }));
        Assert.NotEqual(AccessScopeKey.Of(new[] { "Store A" }), AccessScopeKey.Of(new[] { "Store B" }));
        // "no stores" is not "all stores"
        Assert.NotEqual(AccessScopeKey.Of(null), AccessScopeKey.Of(Array.Empty<string>()));
    }

    // ── same user, different endpoint area ───────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilterLists_AreNotSharedBetweenAnOpenAreaAndARestrictedOne(bool widenedFirst)
    {
        var env = await NewEnvAsync(AccessAreas.Analytics); // Analytics is open => the shared dropdowns widen
        async Task<List<string>> Call(string? area)
        {
            env.Area.Area = area;
            return await env.Dashboard.GetOperationManagersAsync(3, 2026, Role, OmOne);
        }

        List<string> widened, own;
        if (widenedFirst) { widened = await Call(AccessAreas.Shared); own = await Call(null); }
        else { own = await Call(null); widened = await Call(AccessAreas.Shared); }

        Assert.Equal(new[] { "OM One", "OM Two" }, widened);
        Assert.Equal(new[] { "OM One" }, own); // never the widened list that was cached for the other area
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoreList_IsNotSharedBetweenAnOpenAreaAndARestrictedOne(bool widenedFirst)
    {
        var env = await NewEnvAsync(AccessAreas.Analytics);
        async Task<List<StoreReference>> Call(string? area)
        {
            env.Area.Area = area;
            return await env.Stores.GetStoresAsync(3, 2026, Role, OmOne);
        }

        List<StoreReference> widened, own;
        if (widenedFirst) { widened = await Call(AccessAreas.Shared); own = await Call(null); }
        else { own = await Call(null); widened = await Call(AccessAreas.Shared); }

        Assert.Equal(2, widened.Count);
        Assert.Equal(new[] { "Store A" }, own.Select(s => s.StoreName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DashboardChartResults_AreNotSharedBetweenAnOpenAreaAndARestrictedOne(bool openFirst)
    {
        var env = await NewEnvAsync(AccessAreas.Analytics); // Analytics open, Reports restricted
        async Task<int> Headcount(string area)
        {
            env.Area.Area = area;
            var rows = await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, OmOne);
            return rows.Sum(r => r.Value);
        }

        int open, restricted;
        if (openFirst) { open = await Headcount(AccessAreas.Analytics); restricted = await Headcount(AccessAreas.Reports); }
        else { restricted = await Headcount(AccessAreas.Reports); open = await Headcount(AccessAreas.Analytics); }

        Assert.Equal(5, open);       // every store
        Assert.Equal(2, restricted); // Store A only
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExitInterviewResults_AreNotSharedBetweenAnOpenAreaAndARestrictedOne(bool openFirst)
    {
        // The Scorecard profile and the Exit Interviews page call the same service with the same filter.
        var env = await NewEnvAsync(AccessAreas.Scorecard); // Scorecard open, ExitInterviews restricted
        async Task<int> Responses(string area)
        {
            env.Area.Area = area;
            var rows = await env.Exit.GetReasonsForLeavingAsync(new ExitInterviewFilter(), Role, OmOne);
            return rows.Sum(r => r.Value);
        }

        int open, restricted;
        if (openFirst) { open = await Responses(AccessAreas.Scorecard); restricted = await Responses(AccessAreas.ExitInterviews); }
        else { restricted = await Responses(AccessAreas.ExitInterviews); open = await Responses(AccessAreas.Scorecard); }

        Assert.Equal(3, open);
        Assert.Equal(1, restricted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScorecardResults_AreNotSharedBetweenAnOpenAreaAndARestrictedOne(bool openFirst)
    {
        var env = await NewEnvAsync(AccessAreas.Scorecard);
        async Task<List<string>> Leaders(string? area)
        {
            env.Area.Area = area;
            return await env.Scorecard.GetLeaderNamesAsync(Role, OmOne);
        }

        List<string> open, restricted;
        if (openFirst) { open = await Leaders(AccessAreas.Scorecard); restricted = await Leaders(null); }
        else { restricted = await Leaders(null); open = await Leaders(AccessAreas.Scorecard); }

        Assert.Equal(new[] { "Lead A", "Lead B" }, open);
        Assert.Equal(new[] { "Lead A" }, restricted);
    }

    // ── different users ──────────────────────────────────────────────────────

    [Fact]
    public async Task DifferentUsersAndRoles_NeverGetEachOthersCachedResults()
    {
        var env = await NewEnvAsync(); // nothing open: restricted roles keep to their own stores
        env.Area.Area = AccessAreas.Analytics;

        // interleave so that each result is cached before the next caller asks
        var om1 = await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, OmOne);
        var om2 = await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, "om2@example.com");
        var admin = await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, "Admin", "admin@example.com");
        var om1Again = await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, OmOne);
        var stranger = await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, "nobody@example.com");

        Assert.Equal(2, om1.Sum(r => r.Value));
        Assert.Equal(3, om2.Sum(r => r.Value));
        Assert.Equal(5, admin.Sum(r => r.Value));
        Assert.Equal(2, om1Again.Sum(r => r.Value));
        Assert.Empty(stranger); // an email that owns no store gets nothing, not someone else's cached rows
    }

    [Fact]
    public async Task WhenAUsersStoresChange_TheNextRequestIsNotServedTheOldScopesResult()
    {
        // Even with NO invalidation call, a different accessible-store set is a different cache entry.
        var env = await NewEnvAsync();
        env.Area.Area = null;

        var before = await env.Dashboard.GetOperationManagersAsync(3, 2026, Role, OmOne);
        Assert.Equal(new[] { "OM One" }, before);

        var storeB = await env.Db.StoreReferences.SingleAsync(s => s.StoreName == "Store B");
        storeB.OperationManagerEmail = OmOne; // OM One is now also the owner of Store B
        await env.Db.SaveChangesAsync();

        var after = await env.Dashboard.GetOperationManagersAsync(3, 2026, Role, OmOne);
        Assert.Equal(new[] { "OM One", "OM Two" }, after);
    }

    // ── access policy changes ────────────────────────────────────────────────

    [Fact]
    public async Task SavingTheAccessPolicy_InvalidatesTheCache_AndTheNewRulesApplyImmediately()
    {
        var env = await NewEnvAsync(AccessAreas.Analytics);
        env.Area.Area = AccessAreas.Analytics;

        Assert.Equal(5, (await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, OmOne)).Sum(r => r.Value));
        var keyBefore = env.Cache.KeyFor("probe", 1);

        // The Super Admin closes the Analytics area again.
        await env.Policy.SaveAsync(new Dictionary<string, bool> { [AccessAreas.Analytics] = true }, "super-admin");

        Assert.NotEqual(keyBefore, env.Cache.KeyFor("probe", 1)); // every entry cached under the old policy is dropped
        Assert.Equal(2, (await env.Dashboard.GetHeadcountByJobTitleAsync(3, 2026, null, Role, OmOne)).Sum(r => r.Value));
    }

    [Fact]
    public void InvalidateAll_ChangesTheKey_ForTheSameFilters_WithoutAffectingOtherCaches()
    {
        var a = new FilterResultCache();
        var b = new FilterResultCache();
        var keyA = a.KeyFor("kpi", 1, 2026, "x");
        var keyB = b.KeyFor("kpi", 1, 2026, "x");
        Assert.Equal(keyA, keyB);

        a.InvalidateAll();

        Assert.NotEqual(keyA, a.KeyFor("kpi", 1, 2026, "x"));
        Assert.Equal(keyB, b.KeyFor("kpi", 1, 2026, "x")); // per-instance, so tests/instances don't interfere
    }

    [Fact]
    public void AResultLoadedAcrossAnInvalidation_IsNotServedAfterIt()
    {
        // The key is built before the (slow) load, so a load that straddles an invalidation is stored under the
        // OLD version and the next request — which builds a NEW-version key — recomputes.
        var cache = new FilterResultCache();
        var oldKey = cache.KeyFor("slow", 1);
        cache.InvalidateAll(); // an upload lands while the load is still running
        cache.Set(oldKey, "stale", TimeSpan.FromMinutes(5));

        Assert.False(cache.TryGet<string>(cache.KeyFor("slow", 1), out _));
    }

    // ── "latest data period" shown in the layout ─────────────────────────────

    [Fact]
    public async Task DataFreshness_ShowsANewerStoreReferenceUpload_OnceInvalidated()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = "a.xlsx", Month = 3, Year = 2026, UploadedBy = "u" });
        await db.SaveChangesAsync();
        var service = new DataFreshnessService(db, new MemoryCache(new MemoryCacheOptions()));

        DataFreshnessService.InvalidateCache();
        Assert.Equal(new DataFreshnessPeriod(3, 2026), await service.GetLatestDataPeriodAsync());

        // A Store Reference file for a newer period is uploaded (UploadService.UpdateSingleFileAsync calls InvalidateCache).
        db.UploadLogs.Add(new UploadLog { FileType = "store_reference", FileName = "s.xlsx", Month = 4, Year = 2026, UploadedBy = "u" });
        await db.SaveChangesAsync();
        DataFreshnessService.InvalidateCache();

        Assert.Equal(new DataFreshnessPeriod(4, 2026), await service.GetLatestDataPeriodAsync());
    }
}
