using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The Trainer role: sees the stores of the people (any level of the reporting line) and the fixed stores
/// the Admin picked for them, read-only unless the Admin ticks "can edit".</summary>
public class TrainerRoleTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StoreReference Store(int month, string name, string od = "", string om = "", string soc = "", string oc = "", string hm = "") => new()
    {
        Month = month, Year = 2026, StoreName = name,
        OperationDirector = od, OperationDirectorEmail = od == "" ? "" : od + "@x.com",
        OperationManager = om, OperationManagerEmail = om == "" ? "" : om + "@x.com",
        SeniorOperationConsultant = soc, SeniorOperationConsultantEmail = soc == "" ? "" : soc + "@x.com",
        OperationConsultant = oc, OperationConsultantEmail = oc == "" ? "" : oc.ToUpperInvariant() + "@x.com",
        HeadManager = hm, HeadManagerEmail = hm == "" ? "" : hm + "@x.com",
    };

    // Latest period is month 2. Hossam > Nadia > { Ahmed > { Sara > Mona (201,202), Sara (203, no head manager), 101 straight to Ahmed },
    //                                              Omar straight under Nadia (301) } ; 401 has no reporting line at all.
    private static async Task<AppDbContext> SeededDbAsync()
    {
        var db = NewDb();
        db.StoreReferences.AddRange(
            Store(1, "1 | Old", oc: "ali"),
            Store(2, "201 | A", od: "hossam", om: "nadia", soc: "ahmed", oc: "sara", hm: "mona"),
            Store(2, "202 | B", od: "hossam", om: "nadia", soc: "ahmed", oc: "sara", hm: "mona"),
            Store(2, "203 | C", od: "hossam", om: "nadia", soc: "ahmed", oc: "sara"),
            Store(2, "101 | D", od: "hossam", om: "nadia", soc: "ahmed"),
            Store(2, "301 | E", od: "hossam", om: "nadia", oc: "omar"),
            Store(2, "401 | F"));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<List<string>> VisibleAsync(AppDbContext db, string email = "t@x.com") =>
        (await new StoreAccessService(db).GetAccessibleStoreNamesAsync("Trainer", email))!.OrderBy(s => s).ToList();

    [Fact]
    public async Task EachLevelOfTheReportingLine_OpensTheStoresBelowIt()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);

        await trainers.SaveAsync("t@x.com", new[] { "hm|mona@x.com" }, false);
        Assert.Equal(new[] { "201 | A", "202 | B" }, await VisibleAsync(db));

        await trainers.SaveAsync("t@x.com", new[] { "oc|SARA@x.com" }, false);
        Assert.Equal(new[] { "201 | A", "202 | B", "203 | C" }, await VisibleAsync(db));

        await trainers.SaveAsync("t@x.com", new[] { "soc|ahmed@x.com" }, false);
        Assert.Equal(new[] { "101 | D", "201 | A", "202 | B", "203 | C" }, await VisibleAsync(db)); // incl. the store straight under the Senior

        await trainers.SaveAsync("t@x.com", new[] { "om|nadia@x.com" }, false);
        Assert.Equal(5, (await VisibleAsync(db)).Count);               // everything under the Manager, incl. Omar's store

        await trainers.SaveAsync("t@x.com", new[] { "od|hossam@x.com" }, false);
        Assert.Equal(5, (await VisibleAsync(db)).Count);
        Assert.DoesNotContain("401 | F", await VisibleAsync(db)); // no reporting line, not covered by anyone
    }

    [Fact]
    public async Task AFixedStore_IsCovered_AndOnlyWhileItIsInTheLatestPeriod()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);

        await trainers.SaveAsync("t@x.com", new[] { "store|401 | F", "store|1 | Old", "store|999 | Gone" }, false);

        Assert.Equal(new[] { "401 | F" }, await VisibleAsync(db)); // 1 | Old is only in an older period, 999 does not exist
    }

    [Fact]
    public async Task GroupsAndFixedStores_AreCombined_AndOtherTrainersSeeNothing()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        await trainers.SaveAsync("t@x.com", new[] { "hm|mona@x.com", "store|301 | E" }, false);

        Assert.Equal(new[] { "201 | A", "202 | B", "301 | E" }, await VisibleAsync(db));
        Assert.Empty(await VisibleAsync(db, "other@x.com"));
        Assert.Empty((await new StoreAccessService(db).GetAccessibleStoreNamesAsync("Trainer", null))!);
        Assert.True(await new StoreAccessService(db).CanAccessStoreAsync("Trainer", "t@x.com", "301 | E"));
        Assert.False(await new StoreAccessService(db).CanAccessStoreAsync("Trainer", "t@x.com", "203 | C"));
    }

    [Fact]
    public async Task Trainer_IsReadOnlyUnlessCanEditIsTicked()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        var access = new StoreAccessService(db);

        await trainers.SaveAsync("t@x.com", new[] { "oc|sara@x.com" }, canEdit: false);
        Assert.False(await access.CanManageStoreAsync("Trainer", "t@x.com", "201 | A"));

        await trainers.SaveAsync("t@x.com", new[] { "oc|sara@x.com" }, canEdit: true);
        Assert.True(await access.CanManageStoreAsync("Trainer", "t@x.com", "201 | A"));
        Assert.False(await access.CanManageStoreAsync("Trainer", "t@x.com", "301 | E")); // edit rights never reach other stores
    }

    [Fact]
    public async Task Save_ReplacesCoverage_IgnoresBadEntries_AndDropsTheCachedStoreList()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        var access = new StoreAccessService(db, null, null, new MemoryCache(new MemoryCacheOptions()));

        await trainers.SaveAsync("t@x.com", new[] { "oc|sara@x.com" }, false);
        Assert.Equal(3, (await access.GetOwnStoreNamesAsync("Trainer", "t@x.com"))!.Count);

        await trainers.SaveAsync("t@x.com", new[] { "oc|omar@x.com", "nonsense|x", "oc|", "garbage", "", "oc|omar@x.com" }, false);
        Assert.Equal(new[] { "301 | E" }, await access.GetOwnStoreNamesAsync("Trainer", "t@x.com"));
        Assert.Single(db.TrainerCoverages.Where(t => t.TrainerEmail == "t@x.com"));
    }

    [Fact]
    public async Task SavingWithoutReplacingCoverage_OnlyChangesTheEditFlag()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        await trainers.SaveAsync("t@x.com", new[] { "oc|sara@x.com", "store|401 | F" }, canEdit: false);

        await trainers.SaveAsync("t@x.com", null, canEdit: true, replaceCoverage: false); // e.g. the page's script did not run

        var saved = await trainers.GetAsync("t@x.com");
        Assert.True(saved.CanEdit);
        Assert.Equal(new[] { "oc|sara@x.com", "store|401 | F" }, saved.Coverage.OrderBy(c => c));
    }

    [Fact]
    public async Task RenameAndRemove_FollowTheAccount()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        await trainers.SaveAsync("old@x.com", new[] { "oc|sara@x.com", "store|401 | F" }, true);

        await trainers.RenameAsync("old@x.com", "new@x.com");
        var moved = await trainers.GetAsync("new@x.com");
        Assert.Equal(2, moved.Coverage.Count);
        Assert.True(moved.CanEdit);
        Assert.Empty((await trainers.GetAsync("old@x.com")).Coverage);

        await trainers.RemoveAsync("new@x.com");
        var gone = await trainers.GetAsync("new@x.com");
        Assert.Empty(gone.Coverage);
        Assert.False(gone.CanEdit);
    }

    [Fact]
    public async Task Tree_FollowsTheHierarchy_SkipsBlankLevels_AndUsesTheLatestPeriodOnly()
    {
        using var db = await SeededDbAsync();

        var tree = await new TrainerService(db).GetTreeAsync();

        var hossam = Assert.Single(tree.Roots);
        Assert.Equal(("od", "hossam"), (hossam.Kind, hossam.Name));
        var nadia = Assert.Single(hossam.Children);
        Assert.Equal("om", nadia.Kind);
        Assert.Equal(new[] { "ahmed", "omar" }, nadia.Children.Select(c => c.Name)); // Omar (a Consultant) sits straight under the Manager
        Assert.Equal("oc", nadia.Children.Single(c => c.Name == "omar").Kind);

        var ahmed = nadia.Children.Single(c => c.Name == "ahmed");
        Assert.Equal(new[] { "101 | D" }, ahmed.Stores);               // reports straight to the Senior
        var sara = Assert.Single(ahmed.Children);
        Assert.Equal(new[] { "203 | C" }, sara.Stores);                // no Head Manager
        var mona = Assert.Single(sara.Children);
        Assert.Equal(new[] { "201 | A", "202 | B" }, mona.Stores);

        Assert.Equal(new[] { "401 | F" }, tree.Unassigned);
        Assert.DoesNotContain("1 | Old", tree.Unassigned);
    }

    [Fact]
    public async Task Tree_PicksUpANewUpload_OnceTheCacheIsInvalidated()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db, new MemoryCache(new MemoryCacheOptions()));
        Assert.Single((await trainers.GetTreeAsync()).Roots);

        db.StoreReferences.Add(Store(2, "501 | G", od: "newdir"));
        await db.SaveChangesAsync();

        StoreAccessService.InvalidateCache();                          // what a Store Reference upload does
        Assert.Equal(2, (await trainers.GetTreeAsync()).Roots.Count);
    }

    [Fact]
    public void Trainer_IsARecognizedRole_AndAPageRole()
    {
        Assert.True(UserManagementPolicy.IsValidRole("trainer"));
        Assert.Equal("Trainer", UserManagementPolicy.NormalizeRole("trainer"));
        Assert.Contains("Trainer", UserPages.Roles);
        Assert.True(UserManagementPolicy.CanAssignRole("someone@x.com", "Trainer")); // not an Admin-level role
        Assert.True(new StoreAccessService(NewDb()).IsRestrictedRole("Trainer"));
        Assert.Equal(5, new StoreAccessService(NewDb()).RestrictedRoles.Count);      // Trainer has no Store Reference email column
    }
}
