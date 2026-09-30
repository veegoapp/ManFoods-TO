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
