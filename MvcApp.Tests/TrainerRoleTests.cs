using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The Trainer role: sees the stores of the Operation Consultants picked for them, read-only unless
/// the Admin ticks "can edit".</summary>
public class TrainerRoleTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<AppDbContext> SeededDbAsync()
    {
        var db = NewDb();
        db.StoreReferences.AddRange(
            new StoreReference { Month = 1, Year = 2026, StoreName = "1 | Old", OperationConsultant = "Ali", OperationConsultantEmail = "ali@x.com" },
            new StoreReference { Month = 2, Year = 2026, StoreName = "2 | A", OperationConsultant = "Ali", OperationConsultantEmail = "ALI@x.com" },
            new StoreReference { Month = 2, Year = 2026, StoreName = "3 | B", OperationConsultant = "Ali", OperationConsultantEmail = "ali@x.com" },
            new StoreReference { Month = 2, Year = 2026, StoreName = "4 | C", OperationConsultant = "Sara", OperationConsultantEmail = "sara@x.com" },
            new StoreReference { Month = 2, Year = 2026, StoreName = "5 | D", OperationConsultant = "Omar", OperationConsultantEmail = "omar@x.com" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Trainer_SeesOnlyTheStoresOfThePickedConsultants_InTheLatestPeriod()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        await trainers.SaveAsync("Trainer@X.com", new[] { "ali@x.com", "SARA@x.com" }, canEdit: false);
        var access = new StoreAccessService(db);

        var stores = await access.GetAccessibleStoreNamesAsync("Trainer", "trainer@x.com");

        Assert.Equal(new[] { "2 | A", "3 | B", "4 | C" }, stores!.OrderBy(s => s));
        Assert.True(await access.CanAccessStoreAsync("Trainer", "trainer@x.com", "4 | C"));
        Assert.False(await access.CanAccessStoreAsync("Trainer", "trainer@x.com", "5 | D"));
    }

    [Fact]
    public async Task Trainer_WithNoPicks_OrUnknownEmail_SeesNothing()
    {
        using var db = await SeededDbAsync();
        var access = new StoreAccessService(db);

        Assert.Empty((await access.GetAccessibleStoreNamesAsync("Trainer", "nobody@x.com"))!);
        Assert.Empty((await access.GetAccessibleStoreNamesAsync("Trainer", null))!);
        Assert.True(access.IsRestrictedRole("Trainer"));
        Assert.Equal(5, access.RestrictedRoles.Count); // Trainer is not matched against a Store Reference email column
    }

    [Fact]
    public async Task Trainer_IsReadOnlyUnlessCanEditIsTicked()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        var access = new StoreAccessService(db);

        await trainers.SaveAsync("t@x.com", new[] { "ali@x.com" }, canEdit: false);
        Assert.False(await access.CanManageStoreAsync("Trainer", "t@x.com", "2 | A"));

        await trainers.SaveAsync("t@x.com", new[] { "ali@x.com" }, canEdit: true);
        Assert.True(await access.CanManageStoreAsync("Trainer", "t@x.com", "2 | A"));
        Assert.False(await access.CanManageStoreAsync("Trainer", "t@x.com", "4 | C")); // edit rights never reach other stores
    }

    [Fact]
    public async Task Save_ReplacesThePicks_AndDropsTheCachedStoreList()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        var access = new StoreAccessService(db, null, null, new MemoryCache(new MemoryCacheOptions()));

        await trainers.SaveAsync("t@x.com", new[] { "ali@x.com" }, false);
        Assert.Equal(2, (await access.GetOwnStoreNamesAsync("Trainer", "t@x.com"))!.Count);

        await trainers.SaveAsync("t@x.com", new[] { "omar@x.com" }, false);
        Assert.Equal(new[] { "5 | D" }, await access.GetOwnStoreNamesAsync("Trainer", "t@x.com"));
        Assert.Single(db.TrainerConsultants.Where(t => t.TrainerEmail == "t@x.com"));
    }

    [Fact]
    public async Task RenameAndRemove_FollowTheAccount()
    {
        using var db = await SeededDbAsync();
        var trainers = new TrainerService(db);
        await trainers.SaveAsync("old@x.com", new[] { "ali@x.com" }, true);

        await trainers.RenameAsync("old@x.com", "new@x.com");
        var moved = await trainers.GetAsync("new@x.com");
        Assert.Equal(new[] { "ali@x.com" }, moved.ConsultantEmails);
        Assert.True(moved.CanEdit);
        Assert.Empty((await trainers.GetAsync("old@x.com")).ConsultantEmails);

        await trainers.RemoveAsync("new@x.com");
        var gone = await trainers.GetAsync("new@x.com");
        Assert.Empty(gone.ConsultantEmails);
        Assert.False(gone.CanEdit);
    }

    [Fact]
    public async Task ConsultantOptions_ComeFromTheLatestPeriodOnly_OnePerEmail()
    {
        using var db = await SeededDbAsync();
        db.StoreReferences.Add(new StoreReference { Month = 1, Year = 2026, StoreName = "9 | Z", OperationConsultant = "Gone", OperationConsultantEmail = "gone@x.com" });
        await db.SaveChangesAsync();

        var options = await new TrainerService(db).GetConsultantOptionsAsync();

        Assert.Equal(new[] { "ali@x.com", "omar@x.com", "sara@x.com" }, options.Select(o => o.Email).OrderBy(e => e));
        Assert.DoesNotContain(options, o => o.Email == "gone@x.com");
    }

    [Fact]
    public void Trainer_IsARecognizedRole_AndAPageRole()
    {
        Assert.True(UserManagementPolicy.IsValidRole("trainer"));
        Assert.Equal("Trainer", UserManagementPolicy.NormalizeRole("trainer"));
        Assert.Contains("Trainer", UserPages.Roles);
        Assert.True(UserManagementPolicy.CanAssignRole("someone@x.com", "Trainer")); // not an Admin-level role
    }
}
