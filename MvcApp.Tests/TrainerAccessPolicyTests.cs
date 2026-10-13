using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Settings → Access has a second column that applies to the Trainer role only. The regular column and the
/// roles it governs must behave exactly as before.</summary>
public class TrainerAccessPolicyTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AccessPolicyService NewPolicy(AppDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()));

    private static async Task SeedStoresAsync(AppDbContext db)
    {
        db.StoreReferences.AddRange(
            new StoreReference { Month = 2, Year = 2026, StoreName = "201 | A", OperationConsultant = "Sara", OperationConsultantEmail = "sara@x.com" },
            new StoreReference { Month = 2, Year = 2026, StoreName = "301 | B", OperationConsultant = "Omar", OperationConsultantEmail = "omar@x.com" });
        db.TrainerCoverages.Add(new TrainerCoverage { TrainerEmail = "t@x.com", Kind = "oc", Value = "sara@x.com" });
        await db.SaveChangesAsync();
    }

    private static StoreAccessService Access(AppDbContext db, AccessPolicyService policy, string area) =>
        new(db, policy, new AccessAreaContext { Area = area }, null);

    [Fact]
    public async Task TrainerColumn_StartsRestricted_EvenWhenTheRegularColumnIsOpen()
    {
        var db = NewDb();
        var policy = NewPolicy(db);
        db.PageAccessConfigs.Add(new PageAccessConfig { AreaKey = AccessAreas.Analytics, IsRestricted = false, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        Assert.False(await policy.IsRestrictedAsync(AccessAreas.Analytics));                                  // regular column: open
        Assert.True(await policy.IsRestrictedAsync(AccessAreas.TrainerKey(AccessAreas.Analytics)));            // trainer column: never saved = own stores
        Assert.All((await policy.GetAllForTrainerAsync()).Values, restricted => Assert.True(restricted));
        Assert.Equal(AccessAreas.All.Count, (await policy.GetAllForTrainerAsync()).Count);
    }

    [Fact]
    public async Task SavingOneColumn_NeverChangesTheOther()
    {
        var db = NewDb();
        var policy = NewPolicy(db);

        await policy.SaveForTrainerAsync(new() { [AccessAreas.Retention] = false }, "admin");
        Assert.True((await policy.GetAllAsync())[AccessAreas.Retention]);                    // regular column untouched
        Assert.False((await policy.GetAllForTrainerAsync())[AccessAreas.Retention]);

        await policy.SaveAsync(new() { [AccessAreas.Scorecard] = false }, "admin");
        Assert.True((await policy.GetAllForTrainerAsync())[AccessAreas.Scorecard]);          // trainer column untouched
        Assert.False((await policy.GetAllAsync())[AccessAreas.Scorecard]);
    }

    [Fact]
    public async Task SaveChanges_AreReportedPerColumn_ForTheActivityLog()
    {
        var policy = NewPolicy(NewDb());

        var regular = await policy.SaveAsync(new() { [AccessAreas.Retention] = false }, "admin");
        var trainer = await policy.SaveForTrainerAsync(new() { [AccessAreas.Retention] = false }, "admin");

        Assert.Equal(new[] { "retention: own stores → all stores" }, regular);                 // unchanged format
        Assert.Equal(new[] { "trainer retention: own stores → all stores" }, trainer);
        Assert.Empty(await policy.SaveForTrainerAsync(new() { [AccessAreas.Retention] = false }, "admin")); // nothing changed
    }

    [Fact]
    public async Task TrainerRows_NeverCountAsAnOpenAreaForTheOtherRoles()
    {
        var policy = NewPolicy(NewDb());
        await policy.SaveForTrainerAsync(new() { [AccessAreas.Retention] = false }, "admin");

        Assert.False(await policy.AnyOpenAsync());   // only the Trainer column is open: other roles' shared dropdowns stay narrow

        await policy.SaveAsync(new() { [AccessAreas.Scorecard] = false }, "admin");
        Assert.True(await policy.AnyOpenAsync());
    }

    [Fact]
    public async Task Trainer_FollowsItsOwnColumn_AndOtherRolesFollowTheRegularOne()
    {
        var db = NewDb();
        await SeedStoresAsync(db);
        var policy = NewPolicy(db);

        // Regular column open for Retention only; Trainer column open for Scorecard only.
        await policy.SaveAsync(new() { [AccessAreas.Retention] = false }, "admin");
        await policy.SaveForTrainerAsync(new() { [AccessAreas.Scorecard] = false }, "admin");

        // Trainer: restricted where only the regular column is open, open where its own column is.
        Assert.Equal(new[] { "201 | A" }, await Access(db, policy, AccessAreas.Retention).GetAccessibleStoreNamesAsync("Trainer", "t@x.com"));
        Assert.Null(await Access(db, policy, AccessAreas.Scorecard).GetAccessibleStoreNamesAsync("Trainer", "t@x.com"));

        // Operation Consultant: the reverse — the Trainer column means nothing to it.
        Assert.Null(await Access(db, policy, AccessAreas.Retention).GetAccessibleStoreNamesAsync("Operation_Consultant", "sara@x.com"));
        Assert.Equal(new[] { "201 | A" }, await Access(db, policy, AccessAreas.Scorecard).GetAccessibleStoreNamesAsync("Operation_Consultant", "sara@x.com"));
    }

    [Fact]
    public async Task SharedDropdowns_WidenForATrainerOnlyWhenItsOwnColumnHasAnOpenArea()
    {
        var db = NewDb();
        await SeedStoresAsync(db);
        var policy = NewPolicy(db);

        await policy.SaveAsync(new() { [AccessAreas.Retention] = false }, "admin");   // regular column open somewhere
        var shared = Access(db, policy, AccessAreas.Shared);
        Assert.Equal(new[] { "201 | A" }, await shared.GetAccessibleStoreNamesAsync("Trainer", "t@x.com"));
        Assert.Null(await shared.GetAccessibleStoreNamesAsync("Operation_Consultant", "sara@x.com"));

        await policy.SaveForTrainerAsync(new() { [AccessAreas.Scorecard] = false }, "admin");
        Assert.Null(await shared.GetAccessibleStoreNamesAsync("Trainer", "t@x.com"));
    }

    [Fact]
    public async Task Writing_StaysOnTheTrainersOwnStores_EvenWhenItsAreaIsOpen()
    {
        var db = NewDb();
        await SeedStoresAsync(db);
        var policy = NewPolicy(db);
        await new TrainerService(db).SaveAsync("t@x.com", new[] { "oc|sara@x.com" }, canEdit: true);
        await policy.SaveForTrainerAsync(new() { [AccessAreas.ActionCenter] = false }, "admin");
        var access = Access(db, policy, AccessAreas.ActionCenter);

        Assert.Null(await access.GetAccessibleStoreNamesAsync("Trainer", "t@x.com"));                 // can see everything there
        Assert.True(await access.CanManageStoreAsync("Trainer", "t@x.com", "201 | A"));
        Assert.False(await access.CanManageStoreAsync("Trainer", "t@x.com", "301 | B"));              // but only write on its own
    }

    [Fact]
    public async Task ReportExport_ForATrainer_ReadsTheTrainerColumn()
    {
        var policy = NewPolicy(NewDb());
        var access = new ReportAccessService(policy);
        var report = ReportCatalog.Find("workforce-planning")!;

        await policy.SaveAsync(new() { [AccessAreas.WorkforcePlanning] = false }, "admin");            // regular column open
        Assert.Null(await access.ResolveRestrictedAreaAsync(report));                                   // other roles: not restricted
        Assert.Equal(AccessAreas.WorkforcePlanning, await access.ResolveRestrictedAreaAsync(report, forTrainer: true)); // Trainer column still restricted

        await policy.SaveForTrainerAsync(new() { [AccessAreas.WorkforcePlanning] = false }, "admin");
        Assert.Null(await access.ResolveRestrictedAreaAsync(report, forTrainer: true));
    }
}
