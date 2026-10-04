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
    public async Task DefaultPeriod_IsTheLatestMonthWithAnUploadedRoster()
    {
        var db = NewDb();
        // The projection covers the whole year; rosters are only uploaded up to March. Whatever today's date is,
        // the page must open on March (the latest month with real data), not on the calendar month.
        for (int m = 1; m <= 12; m++) Proj(db, m, "1 | A", "Crew", 10);
        Active(db, 1, "1 | A", "Crew", 9); Active(db, 2, "1 | A", "Crew", 9); Active(db, 3, "1 | A", "Crew", 8);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(null, null, null, null, "Admin", null);

        Assert.Equal(2026, dto.Year);
        Assert.Equal(3, dto.Month);
        Assert.True(dto.HasActual);
        Assert.Equal(8, dto.Kpis.Actual);

        var chosen = await NewService(db).GetAsync(2026, 7, null, null, "Admin", null); // an explicit month is still honoured
        Assert.Equal(7, chosen.Month);
        Assert.False(chosen.HasActual);
    }

    [Fact]
    public async Task HiringNeed_IsAlwaysShortagePlusExpectedResignations()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "MDS", 2);          // Crew is 3 short, MDS has 1 too many
        Active(db, 1, "1 | A", "Crew", 7); Active(db, 1, "1 | A", "MDS", 3);
        db.Resignations.Add(new Resignation { Year = 2026, Month = 1, EmployeeId = "r1", Store = "1 | A", JobTitle = "MDS" });   // expected: 1 a month, from the job that has a surplus
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(3, dto.Kpis.Shortage);
        Assert.Equal(1, dto.Kpis.ExpectedAttrition);
        Assert.Equal(4, dto.Kpis.HiringNeed);                                      // 3 + 1, even though the leaver is from a job above its plan
        var store = Assert.Single(dto.ByStore);
        Assert.Equal(store.Shortage + Math.Round(store.ExpectedAttrition, MidpointRounding.AwayFromZero), store.HiringNeed);
        foreach (var g in dto.ByPayrollGroup)
            Assert.Equal(g.Shortage + Math.Round(g.ExpectedAttrition, MidpointRounding.AwayFromZero), g.HiringNeed);

        var detail = await NewService(db).GetDetailAsync(2026, new[] { 1 }, null, null, "Admin", null);
        Assert.Equal(4.0, detail.Sum(r => r.HiringNeed ?? 0));                      // the per-job rows add up to the same
    }

    [Fact]
    public async Task GapAndFillRate_AreActualMinusProjected()
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
        Assert.Equal(-2, dto.Kpis.Gap); // shortage is negative, surplus positive
        Assert.Equal(91.7, dto.Kpis.FillPercent);
        var crew = Assert.Single(dto.ByJob, r => r.Name == "Crew");
        Assert.Equal(20, crew.Projected); Assert.Equal(18, crew.Actual); Assert.Equal(-2, crew.Gap);
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
        Assert.Equal(-3, crew.Gap);
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
    public async Task StoreRows_CarryTheOperationConsultant_PreferringThePlannedPeriod()
    {
        var db = NewDb();
        Proj(db, 3, "1 | A", "Crew", 10); Proj(db, 3, "2 | B", "Crew", 10); Proj(db, 3, "3 | C", "Crew", 10);
        db.StoreReferences.Add(new StoreReference { Year = 2026, Month = 2, StoreName = "1 | A", OperationConsultant = "Old OC" });
        db.StoreReferences.Add(new StoreReference { Year = 2026, Month = 3, StoreName = "1 | A", OperationConsultant = "March OC" });
        db.StoreReferences.Add(new StoreReference { Year = 2026, Month = 2, StoreName = "2 | B", OperationConsultant = "Feb OC" }); // no March entry -> latest before
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 3, null, null, "Admin", null);

        Assert.Equal("March OC", dto.ByStore.Single(r => r.Name == "1 | A").OperationConsultant);
        Assert.Equal("Feb OC", dto.ByStore.Single(r => r.Name == "2 | B").OperationConsultant);
        Assert.Equal("", dto.ByStore.Single(r => r.Name == "3 | C").OperationConsultant);
    }

    [Fact]
    public async Task RollUps_SumTheStoresUnderEachConsultantManagerAndDirector()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "2 | B", "Crew", 20); Proj(db, 1, "3 | C", "Crew", 6); Proj(db, 1, "4 | D", "Crew", 5);
        Active(db, 1, "1 | A", "Crew", 8); Active(db, 1, "2 | B", "Crew", 20); Active(db, 1, "3 | C", "Crew", 3); Active(db, 1, "4 | D", "Crew", 5);
        db.StoreReferences.AddRange(
            new StoreReference { Year = 2026, Month = 1, StoreName = "1 | A", OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
            new StoreReference { Year = 2026, Month = 1, StoreName = "2 | B", OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
            new StoreReference { Year = 2026, Month = 1, StoreName = "3 | C", OperationConsultant = "Bob", OperationManager = "Mona", OperationDirector = "Eve", SeniorOperationConsultant = "Sam" });
        // store 4 has no Store Reference entry at all -> left out of every roll-up
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        var amy = Assert.Single(dto.ByOperationConsultant, r => r.Name == "Amy");
        Assert.Equal(2, amy.StoreCount); Assert.Equal(30, amy.Projected); Assert.Equal(28, amy.Actual); Assert.Equal(-2, amy.Gap);
        Assert.Equal(2, amy.HiringNeed);                 // store 2 is at plan (0), store 1 short by 2
        var bob = Assert.Single(dto.ByOperationConsultant, r => r.Name == "Bob");
        Assert.Equal(-3, bob.Gap); Assert.Equal("critical", bob.Status); // 3 of 6 = 50%

        var mona = Assert.Single(dto.ByOperationManager);
        Assert.Equal(3, mona.StoreCount); Assert.Equal(36, mona.Projected); Assert.Equal(31, mona.Actual);
        Assert.Equal(2, dto.ByOperationDirector.Count);
        Assert.Equal("Eve", dto.ByOperationDirector[0].Name);              // shortage 3 beats Dan's 2: largest shortage first
    }

    private static async Task<AppDbContext> LeadershipDb()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "2 | B", "Crew", 20); Proj(db, 1, "3 | C", "Crew", 6); Proj(db, 1, "4 | D", "Crew", 5);
        Active(db, 1, "1 | A", "Crew", 8); Active(db, 1, "2 | B", "Crew", 20); Active(db, 1, "3 | C", "Crew", 3); Active(db, 1, "4 | D", "Crew", 5);
        db.StoreReferences.AddRange(
            new StoreReference { Year = 2026, Month = 1, StoreName = "1 | A", OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
            new StoreReference { Year = 2026, Month = 1, StoreName = "2 | B", OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
            new StoreReference { Year = 2026, Month = 1, StoreName = "3 | C", OperationConsultant = "Bob", OperationManager = "Mona", OperationDirector = "Eve", SeniorOperationConsultant = "Sue" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task LeadershipFilters_NarrowEveryFigure_AndListTheirOptions()
    {
        var db = await LeadershipDb();
        var svc = NewService(db);

        var all = await svc.GetAsync(2026, 1, null, null, "Admin", null);
        Assert.Equal(new[] { "Amy", "Bob" }, all.OperationConsultants);
        Assert.Equal(new[] { "Mona" }, all.OperationManagers);
        Assert.Equal(new[] { "Sam", "Sue" }, all.SeniorOperationConsultants);
        Assert.Equal(new[] { "Dan", "Eve" }, all.OperationDirectors);

        var amy = await svc.GetAsync(2026, 1, null, null, "Admin", null, oc: "Amy");
        Assert.Equal(30, amy.Kpis.Projected);
        Assert.Equal(new[] { "1 | A", "2 | B" }, amy.ByStore.Select(r => r.Name).OrderBy(n => n));
        Assert.Equal(30, amy.Trend.Single(t => t.Month == 1).Projected);
        Assert.Equal("Amy", Assert.Single(amy.ByOperationConsultant).Name);

        // Filters combine (AND), and a comma list means "any of".
        Assert.Empty((await svc.GetAsync(2026, 1, null, null, "Admin", null, oc: "Amy", od: "Eve")).ByStore);
        Assert.Equal(3, (await svc.GetAsync(2026, 1, null, null, "Admin", null, oc: "Amy,Bob")).ByStore.Count);
        Assert.Equal(2, (await svc.GetAsync(2026, 1, null, null, "Admin", null, soc: "Sam")).ByStore.Count);
    }

    [Fact]
    public async Task LeadershipFilter_ExcludesStoresWithoutAStoreReferenceEntry()
    {
        var db = await LeadershipDb();
        var withFilter = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null, om: "Mona");
        Assert.DoesNotContain(withFilter.ByStore, r => r.Name == "4 | D");   // store 4 has no Store Reference entry
        Assert.Contains((await NewService(db).GetAsync(2026, 1, null, null, "Admin", null)).ByStore, r => r.Name == "4 | D");
    }

    [Fact]
    public async Task DetailRows_HonourTheLeadershipFilters()
    {
        var db = await LeadershipDb();
        var rows = await NewService(db).GetDetailAsync(2026, null, null, null, "Admin", null, oc: "Bob");
        var row = Assert.Single(rows);
        Assert.Equal("3 | C", row.Store);
        Assert.Equal("Bob", row.OperationConsultant); Assert.Equal("Mona", row.OperationManager);
        Assert.Equal("", row.PayrollGroup); // no roster job mapping in this data set
        Assert.Equal("Sue", row.SeniorOperationConsultant); Assert.Equal("Eve", row.OperationDirector);
    }

    [Fact]
    public async Task Surplus_IsPositive_AndAbove100PercentIsOverstaffed()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "2 | B", "Crew", 10); Proj(db, 1, "3 | C", "Crew", 10); Proj(db, 1, "4 | D", "Crew", 10);
        Active(db, 1, "1 | A", "Crew", 12);  // over plan
        Active(db, 1, "2 | B", "Crew", 10);  // exactly on plan
        Active(db, 1, "3 | C", "Crew", 9);   // 90% -> watch
        Active(db, 1, "4 | D", "Crew", 8);   // 80% -> critical
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        var a = dto.ByStore.Single(r => r.Name == "1 | A");
        Assert.Equal(2, a.Gap); Assert.Equal("over", a.Status);
        Assert.Equal("ok", dto.ByStore.Single(r => r.Name == "2 | B").Status);      // up to and including 100%
        Assert.Equal("watch", dto.ByStore.Single(r => r.Name == "3 | C").Status);
        Assert.Equal("critical", dto.ByStore.Single(r => r.Name == "4 | D").Status);
        Assert.Equal("4 | D", dto.ByStore[0].Name);                                 // largest shortage first
        Assert.Equal("1 | A", dto.ByStore[^1].Name);                                // biggest surplus last
    }

    [Fact]
    public async Task Shortage_SumsOnlyTheRealShortages_AndExplainsTheHiringNeed()
    {
        var db = NewDb();
        // One store: Crew is 4 short, Crew Trainer is 6 over -> the gap nets to +2 but the shortage is still 4.
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "Crew Trainer", 3);
        Active(db, 1, "1 | A", "Crew", 6); Active(db, 1, "1 | A", "Crew Trainer", 9);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(2, dto.Kpis.Gap);                 // 15 actual vs 13 projected
        Assert.Equal(4, dto.Kpis.Shortage);            // only Crew is short
        Assert.Equal(4, dto.Kpis.HiringNeed);          // no resignation history -> need = shortage
        Assert.Equal(4, dto.ByStore.Single().Shortage);
        Assert.Equal(4, dto.ByJob.Single(r => r.Name == "Crew").Shortage);
        Assert.Equal(0, dto.ByJob.Single(r => r.Name == "Crew Trainer").Shortage);
    }

    [Fact]
    public async Task PayrollGroups_SumTheirJobs_UsingTheGroupLearnedFromTheRoster()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "Crew Trainer", 4); Proj(db, 1, "1 | A", "GEM", 3); Proj(db, 1, "1 | A", "Newly Added Job", 2);
        Proj(db, 1, "2 | B", "Crew", 6);
        void Emp(string store, string job, string group, int count)
        {
            for (int i = 0; i < count; i++)
                db.ActiveEmployees.Add(new ActiveEmployee { Year = 2026, Month = 1, Store = store, JobTitle = job, PayrollGroup = group, EmployeeId = Guid.NewGuid().ToString() });
        }
        Emp("1 | A", "Crew", "Hourly", 8); Emp("1 | A", "Crew Trainer", "Hourly", 6); Emp("1 | A", "GEM", "Monthly", 3); Emp("2 | B", "Crew", "Hourly", 6);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        var hourly = Assert.Single(dto.ByPayrollGroup, r => r.Name == "Hourly");
        Assert.Equal(20, hourly.Projected);            // Crew 10 + Crew Trainer 4 + Crew (store 2) 6
        Assert.Equal(20, hourly.Actual);                // 8 + 6 + 6
        Assert.Equal(0, hourly.Gap);
        Assert.Equal(2, hourly.Shortage);               // Crew in store 1 is 2 short; the trainer surplus does not offset it
        var monthly = Assert.Single(dto.ByPayrollGroup, r => r.Name == "Monthly");
        Assert.Equal(3, monthly.Projected); Assert.Equal(3, monthly.Actual);
        var unassigned = Assert.Single(dto.ByPayrollGroup, r => r.Name == "");   // a job nobody works yet has no known group
        Assert.Equal(2, unassigned.Projected);
        Assert.Equal(dto.Kpis.Projected, dto.ByPayrollGroup.Sum(r => r.Projected));
    }

    [Fact]
    public async Task DetailRows_CarryThePayrollGroupOfTheirJob()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "Brand New Job", 2);
        db.ActiveEmployees.Add(new ActiveEmployee { Year = 2026, Month = 1, Store = "1 | A", JobTitle = "Crew", PayrollGroup = "Hourly", EmployeeId = "E1" });
        await db.SaveChangesAsync();

        var rows = await NewService(db).GetDetailAsync(2026, null, null, null, "Admin", null);

        Assert.Equal("Hourly", rows.Single(r => r.Job == "Crew").PayrollGroup);
        Assert.Equal("", rows.Single(r => r.Job == "Brand New Job").PayrollGroup);
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

    [Fact]
    public async Task HiringForecast_LeaderRollUps_AddUpTheStoreMonthsAndSkipBlankLeaders()
    {
        var db = await LeadershipDb();
        Proj(db, 1, "9 | Z", "Crew", 4); Active(db, 1, "9 | Z", "Crew", 1); // no store reference: no leader at all
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null);

        // A needs 2, B 0, C 3, D 0 (and Z 3, with no leader); Amy leads A+B, Bob leads C.
        var amy = Assert.Single(dto.ByOperationConsultant, r => r.Store == "Amy");
        Assert.Equal(2, amy.StoreCount); Assert.Equal(2, amy.Months[0]); Assert.Equal(2, amy.Total);
        Assert.Equal(3, Assert.Single(dto.ByOperationConsultant, r => r.Store == "Bob").Total);
        Assert.Equal(new[] { "Bob", "Amy" }, dto.ByOperationConsultant.Select(r => r.Store));   // biggest need first
        Assert.Equal(new[] { "Mona" }, dto.ByOperationManager.Select(r => r.Store));
        Assert.Equal(5, dto.ByOperationManager[0].Total);
        Assert.Equal(new[] { "Eve", "Dan" }, dto.ByOperationDirector.Select(r => r.Store));
        Assert.Equal(new[] { "Sue", "Sam" }, dto.BySeniorOperationConsultant.Select(r => r.Store));
        Assert.All(new[] { dto.ByOperationConsultant, dto.ByOperationManager, dto.ByOperationDirector, dto.BySeniorOperationConsultant },
            g => Assert.Equal(5, g.Sum(r => r.Total)));                                          // the leaderless store is not in any roll-up
        Assert.Equal(8, dto.GrandTotal);

        // Same figures whatever the main table is grouped by, and the filters narrow them like the table.
        var byJob = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null, by: "job");
        Assert.Equal(dto.ByOperationConsultant.Select(r => (r.Store, r.Total)), byJob.ByOperationConsultant.Select(r => (r.Store, r.Total)));
        var onlyAmy = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null, oc: "Amy");
        Assert.Equal("Amy", Assert.Single(onlyAmy.ByOperationConsultant).Store);
    }

    [Fact]
    public async Task HiringForecast_PastMonthUsesNeedFormula_FutureMonthsAreSimulated()
    {
        var db = NewDb();
        // Jan: target 10, have 8. Feb: target 10. Mar: target 12.
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 2, "1 | A", "Crew", 10); Proj(db, 3, "1 | A", "Crew", 12);
        Active(db, 1, "1 | A", "Crew", 8);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null);

        Assert.True(dto.HasRoster);
        var row = Assert.Single(dto.Rows);
        Assert.Equal(2, row.Months[0]);          // Jan (roster): 10 - 8, no resignations history
        Assert.Equal(2, row.Months[1]);          // Feb: still 8 people, need 2 to reach 10
        Assert.Equal(2, row.Months[2]);          // Mar: 10 people after Feb, need 2 more to reach 12
        Assert.Equal(6, row.Total);
        Assert.Equal(6, dto.GrandTotal);
        Assert.Equal("actual", dto.MonthModes[0]);
        Assert.Equal("forecast", dto.MonthModes[1]);
        Assert.Equal("none", dto.MonthModes[5]);
    }

    [Fact]
    public async Task HiringForecast_EarlyLeaverRate_GrossesHiresUp()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10);
        Active(db, 1, "1 | A", "Crew", 5);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null, earlyLeaverPercent: 50);

        Assert.Equal(10, Assert.Single(dto.Rows).Months[0]); // 5 net hires / (1 - 0.5)
    }

    [Fact]
    public async Task HiringForecast_ByJob_GroupsRowsByJobAndKeepsTheSameTotal()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10); Proj(db, 1, "1 | A", "GEM", 4); Proj(db, 1, "2 | B", "Crew", 6);
        Active(db, 1, "1 | A", "Crew", 8); Active(db, 1, "1 | A", "GEM", 3); Active(db, 1, "2 | B", "Crew", 6);
        await db.SaveChangesAsync();

        var byStore = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null);
        var byJob = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null, by: "job");

        Assert.Equal("job", byJob.By);
        Assert.Equal(new[] { "Crew", "GEM" }, byJob.Rows.Select(r => r.Store).ToArray());
        Assert.Equal(2, byJob.Rows.Single(r => r.Store == "Crew").Months[0]);
        Assert.Equal(1, byJob.Rows.Single(r => r.Store == "GEM").Months[0]);
        Assert.Equal(byStore.GrandTotal, byJob.GrandTotal);
    }

    [Fact]
    public async Task HiringForecast_ByJob_RoundsAtStoreMonthLevelSoTotalsMatchTheStoreView()
    {
        var db = NewDb();
        // Three jobs each need 2 hires; grossed up by 25% early leavers that is 2.67 each = 8 for the store,
        // but rounding each job on its own would give 9.
        Proj(db, 1, "1 | A", "X", 2); Proj(db, 1, "1 | A", "Y", 2); Proj(db, 1, "1 | A", "Z", 2);
        Active(db, 1, "1 | A", "Other", 1);
        await db.SaveChangesAsync();

        var byStore = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null, earlyLeaverPercent: 25);
        var byJob = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null, earlyLeaverPercent: 25, by: "job");

        Assert.Equal(8, byStore.GrandTotal);
        Assert.Equal(8, byJob.GrandTotal);
        Assert.Equal(8, byJob.Rows.Sum(r => r.Months[0]));
        Assert.Equal(byStore.MonthTotals, byJob.MonthTotals);
    }

    [Fact]
    public async Task TrainerJobs_StaySeparate_EachWithItsOwnProjection()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew Trainer", 2); Proj(db, 1, "1 | A", "Hourly Paid Crew Trainer", 3);
        Proj(db, 1, "2 | B", "hourly paid crew trainer", 1); Proj(db, 1, "1 | A", "Crew", 10);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(16, dto.Kpis.Projected);
        Assert.Equal(2, Assert.Single(dto.ByJob, r => r.Name == "Crew Trainer").Projected);
        Assert.Equal(4, Assert.Single(dto.ByJob, r => r.Name.Equals("Hourly Paid Crew Trainer", StringComparison.OrdinalIgnoreCase)).Projected);
    }

    [Fact]
    public async Task Trainers_GoToTheTrainerJobOfTheirPayrollGroup_WhateverTheirOwnJob()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew Trainer", 2); Proj(db, 1, "1 | A", "Hourly Paid Crew Trainer", 2); Proj(db, 1, "1 | A", "Crew", 5);
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "Crew Trainer", PayrollGroup = "Manfoods Company" });
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "hourly paid crew trainer", PayrollGroup = "Hourly Paid" });
        Person(db, 1, "1", "1 | A", "Crew"); Person(db, 1, "2", "1 | A", "Hostess"); Person(db, 1, "3", "1 | A", "Hourly Paid Crew"); Person(db, 1, "4", "1 | A", "Crew");
        Trainer(db, 1, "1", "1 | A", "Manfoods Company");
        Trainer(db, 1, "2", "1 | A", "manfoods company ");       // another job, same group: still Crew Trainer (case / spacing ignored)
        Trainer(db, 1, "3", "1 | A", "Hourly Paid");
        Trainer(db, 1, "4", "1 | A", "Contractor");               // not a trainer group: stays where it is
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(2, Assert.Single(dto.ByJob, r => r.Name == "Crew Trainer").Actual);
        Assert.Equal(1, Assert.Single(dto.ByJob, r => r.Name.Equals("Hourly Paid Crew Trainer", StringComparison.OrdinalIgnoreCase)).Actual);
        Assert.Equal(1, Assert.Single(dto.ByJob, r => r.Name == "Crew").Actual);        // 2 Crew - the one who is a trainer
        Assert.Equal(4, dto.Kpis.Actual);                                               // total headcount unchanged
        Assert.Equal(2, Assert.Single(dto.ByPayrollGroup, r => r.Name == "Manfoods Company").Projected);   // Crew Trainer sits in its own group
    }

    private static void Person(AppDbContext db, int month, string id, string store, string job, string group = "Hourly") =>
        db.ActiveEmployees.Add(new ActiveEmployee { Year = 2026, Month = month, EmployeeId = id, Store = store, JobTitle = job, PayrollGroup = group });

    private static void Trainer(AppDbContext db, int month, string id, string store, string group = "Manfoods Company") =>
        db.CrewTrainerEmployees.Add(new CrewTrainerEmployee { Year = 2026, Month = month, EmployeeId = id, StoreName = store, PayrollGroup = group });

    [Fact]
    public async Task CrewTrainerActual_MovesListedRosterPeopleOutOfTheirJob()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew Trainer", 2); Proj(db, 1, "1 | A", "Hourly Paid Crew Trainer", 1); Proj(db, 1, "1 | A", "Crew", 10);
        Proj(db, 2, "1 | A", "Crew Trainer", 3); Proj(db, 3, "1 | A", "Crew Trainer", 3);
        Person(db, 1, "1", "1 | A", "Crew"); Person(db, 1, "2", "1 | A", "Crew"); Person(db, 1, "3", "1 | A", "Crew");
        Person(db, 2, "1", "1 | A", "Crew");
        Trainer(db, 1, "1", "1 | A"); Trainer(db, 1, "2", "1 | A");
        Trainer(db, 1, "99", "1 | A");   // not on the January roster: not counted
        Trainer(db, 3, "1", "1 | A");    // no roster for March
        await db.SaveChangesAsync();

        var jan = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);
        var trainer = Assert.Single(jan.ByJob, r => r.Name == "Crew Trainer");
        Assert.Equal(2, trainer.Projected);
        Assert.Equal(2, trainer.Actual);
        Assert.Equal(1, Assert.Single(jan.ByJob, r => r.Name == "Crew").Actual);    // 3 roster Crew - 2 trainers
        Assert.Equal(3, jan.Kpis.Actual);                                          // total headcount is unchanged

        var feb = await NewService(db).GetAsync(2026, 2, null, null, "Admin", null);
        Assert.Equal(0, Assert.Single(feb.ByJob, r => r.Name == "Crew Trainer").Actual); // no list uploaded for February

        var mar = await NewService(db).GetAsync(2026, 3, null, null, "Admin", null);
        Assert.False(mar.HasActual); // a trainer list alone does not make March an "actual" month
    }

    [Fact]
    public async Task PayrollGroup_ComesFromTheReferenceList_AndFallsBackToTheRoster()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 5); Proj(db, 1, "1 | A", "MDS", 2); Proj(db, 1, "1 | A", "NGBL Swing", 1);
        // On the roster Crew is mostly "Hourly" and MDS is "Hourly"; the reference says otherwise for Crew.
        Person(db, 1, "1", "1 | A", "Crew", "Hourly"); Person(db, 1, "2", "1 | A", "Crew", "Hourly"); Person(db, 1, "3", "1 | A", "MDS", "Hourly");
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "CREW ", PayrollGroup = "Manfoods Company" });     // case / spacing tolerant
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "ngbl swing", PayrollGroup = "Hourly Paid" });
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(5, Assert.Single(dto.ByPayrollGroup, r => r.Name == "Manfoods Company").Projected); // Crew: reference beats the roster
        Assert.Equal(2, Assert.Single(dto.ByPayrollGroup, r => r.Name == "Hourly").Projected);            // MDS is not on the list: learned from the roster
        Assert.Equal(1, Assert.Single(dto.ByPayrollGroup, r => r.Name == "Hourly Paid").Projected);       // a job nobody works yet still gets its group
        Assert.DoesNotContain(dto.ByPayrollGroup, r => r.Name == "");
    }

    [Fact]
    public async Task CrewTrainer_ExpectedResignationsAreFoundByEmployeeId()
    {
        var db = NewDb();
        Proj(db, 2, "1 | A", "Crew Trainer", 3); Proj(db, 2, "1 | A", "Crew", 5);
        Person(db, 1, "1", "1 | A", "Crew"); Person(db, 1, "2", "1 | A", "Crew");
        Person(db, 2, "2", "1 | A", "Crew");
        Trainer(db, 1, "1", "1 | A");                       // a trainer in January...
        db.Resignations.Add(new Resignation { Year = 2026, Month = 2, EmployeeId = "1", Store = "1 | A", JobTitle = "Crew" }); // ...who resigned in February
        db.Resignations.Add(new Resignation { Year = 2026, Month = 2, EmployeeId = "2", Store = "1 | A", JobTitle = "Crew" }); // an ordinary Crew resignation
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 2, null, null, "Admin", null);

        // two roster months (Jan, Feb) -> 1 trainer resignation / 2 months = 0.5 expected per month
        Assert.Equal(0.5, Assert.Single(dto.ByJob, r => r.Name == "Crew Trainer").ExpectedAttrition);
        Assert.Equal(0.5, Assert.Single(dto.ByJob, r => r.Name == "Crew").ExpectedAttrition); // the trainer's resignation left Crew
    }

    [Fact]
    public async Task TrainerJobs_AreCountedTogether_ForShortageHiringNeedAndTheForecast()
    {
        var db = NewDb();
        // The projection plans one Crew Trainer; the store hired one Hourly Paid trainer instead. Together: nothing missing.
        Proj(db, 1, "1 | A", "Crew Trainer", 1); Proj(db, 1, "1 | A", "Hourly Paid Crew Trainer", 0);
        Person(db, 1, "1", "1 | A", "Hourly Paid Crew", "Hourly Paid");
        Trainer(db, 1, "1", "1 | A", "Hourly Paid");
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(0, dto.Kpis.Shortage);
        Assert.Equal(0, dto.Kpis.HiringNeed);
        Assert.Equal(1, Assert.Single(dto.ByJob, r => r.Name.Equals("Hourly Paid Crew Trainer", StringComparison.OrdinalIgnoreCase)).Actual); // the jobs themselves still show separately
        Assert.Equal(1, Assert.Single(dto.ByJob, r => r.Name == "Crew Trainer").Projected);
        var detail = await NewService(db).GetDetailAsync(2026, new[] { 1 }, null, null, "Admin", null);
        Assert.Equal(0.0, detail.Sum(r => r.Shortage ?? 0));

        var forecast = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null);
        Assert.Equal(0, Assert.Single(forecast.Rows).Months[0]);
    }

    [Fact]
    public async Task TrainerShortage_StillCountsWhenBothTrainerJobsTogetherAreShort()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew Trainer", 2); Proj(db, 1, "1 | A", "Hourly Paid Crew Trainer", 1);
        Person(db, 1, "1", "1 | A", "Hourly Paid Crew", "Hourly Paid");
        Trainer(db, 1, "1", "1 | A", "Hourly Paid");
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(2, dto.Kpis.Shortage);          // 3 planned, 1 present
        Assert.Equal(2, dto.Kpis.HiringNeed);
    }

    [Fact]
    public async Task HiringForecast_NoRoster_HasNoRows()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew", 10);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetHiringForecastAsync(2026, null, null, "Admin", null);

        Assert.True(dto.HasData);
        Assert.False(dto.HasRoster);
        Assert.Empty(dto.Rows);
    }
}

public class WorkforcePlanningReportTests
{
    private sealed class FakePlanning : IWorkforcePlanningService
    {
        public List<MvcApp.Models.ViewModels.PlanningDetailRow> Rows { get; set; } = new();
        public Task<MvcApp.Models.ViewModels.WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null) => throw new NotSupportedException();
        public Task<List<MvcApp.Models.ViewModels.PlanningDetailRow>> GetDetailAsync(int year, IReadOnlyCollection<int>? months, string? stores, string? jobs, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null) => Task.FromResult(Rows);
        public Task<List<MvcApp.Models.ViewModels.StoreFillDto>> GetStoreFillAsync(int year, int month, string? jobs, string role, string? assignedName) => throw new NotSupportedException();
        public Task<MvcApp.Models.ViewModels.StorePlanDto> GetStorePlanAsync(string store, int year, int month, string role, string? assignedName) => throw new NotSupportedException();
        public Task<List<MvcApp.Models.ViewModels.PeriodItem>> GetProjectionPeriodsAsync() => Task.FromResult(new List<MvcApp.Models.ViewModels.PeriodItem>());
        public Task<List<string>> GetProjectionJobsAsync() => Task.FromResult(new List<string>());
        public Task<MvcApp.Models.ViewModels.HiringForecastDto> GetHiringForecastAsync(int? year, string? stores, string? jobs, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, double earlyLeaverPercent = 0, string? by = null) => Task.FromResult(new MvcApp.Models.ViewModels.HiringForecastDto());
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
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "Crew", PayrollGroup = "Hourly", Projected = 10, Actual = 8, Shortage = 2, ExpectedAttrition = 1.5, HiringNeed = 3.5, OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "GEM", Projected = 4, Actual = 4, Shortage = 0, ExpectedAttrition = 0.5, HiringNeed = 0.5, OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
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
        Assert.Equal(5, names.Count(n => n.StartsWith("Pivot")));
        Assert.Equal(4, reopened.Worksheet("Data").LastRowUsed()!.RowNumber()); // header + 3 rows

        // Hiring need columns: Data sheet, month summary and per-store breakdown.
        var dataWs = reopened.Worksheet("Data");
        Assert.Equal("Hiring Need", dataWs.Cell(1, 11).GetString());
        Assert.Equal(3.5, dataWs.Cell(2, 11).GetDouble());
        var summary = reopened.Worksheet("Summary");
        Assert.Equal(2, summary.Cell(10, 6).GetDouble());   // Jan shortage: Crew is 2 short, GEM is at plan
        Assert.Equal(2, summary.Cell(10, 7).GetDouble());   // expected resignations 1.5 + 0.5
        Assert.Equal(4, summary.Cell(10, 8).GetDouble());   // hiring need 3.5 + 0.5 -> 4
        Assert.Equal(-2, dataWs.Cell(2, 8).GetDouble());   // Data gap = actual − projected (8 − 10)
        Assert.Equal("Shortage", dataWs.Cell(1, 16).GetString());
        Assert.Equal("Payroll Group", dataWs.Cell(1, 17).GetString());
        Assert.Equal("Hourly", dataWs.Cell(2, 17).GetString());
        Assert.Equal("Unassigned", dataWs.Cell(3, 17).GetString()); // a job with no known group
        Assert.Equal(2, dataWs.Cell(2, 16).GetDouble());
        // By Consultant & Manager: four stacked tables; Amy covers 1 store, 14 projected, 12 actual.
        var groups = reopened.Worksheet("By Consultant & Manager");
        var cells = groups.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Operation Consultants", cells); Assert.Contains("Operation Directors", cells);
        Assert.Contains("Operation Managers", cells); Assert.Contains("Senior Operation Consultants", cells);
        var amyRow = groups.RowsUsed().First(r => r.Cell(1).GetString() == "Amy");
        Assert.Equal(1, amyRow.Cell(2).GetDouble()); Assert.Equal(14, amyRow.Cell(3).GetDouble()); Assert.Equal(12, amyRow.Cell(4).GetDouble());
        Assert.Equal(2, amyRow.Cell(7).GetDouble()); // shortage
        Assert.Equal(4, amyRow.Cell(9).GetDouble()); // hiring need 3.5 + 0.5
        Assert.Equal("Operation Consultant", dataWs.Cell(1, 12).GetString());
        Assert.Equal("Amy", dataWs.Cell(2, 12).GetString());
        var byStore = reopened.Worksheets.First(w => w.Name.StartsWith("By Store"));
        Assert.Equal("Shortage", byStore.Cell(1, 6).GetString());
        Assert.Equal(2, byStore.Cell(2, 6).GetDouble());
        Assert.Equal("Hiring need (est.)", byStore.Cell(1, 8).GetString());
        Assert.Equal(4, byStore.Cell(2, 8).GetDouble());

        // …and the raw package must really contain pivot table parts.
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms);
        Assert.Equal(5, zip.Entries.Count(e => e.FullName.StartsWith("xl/pivotTables/pivotTable")));
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

/// <summary>Reading the monthly Crew Trainer allowance list.</summary>
public class CrewTrainerUploadParseTests
{
    private static byte[] Book(string[] headers, params string[][] rows)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static readonly Dictionary<string, string> Known = new() { ["1480001 | merghany"] = "1480001 | Merghany" };

    [Fact]
    public void Reads_ByHeader_RespellsStore_AndCountsRepeatedIdOnce()
    {
        var bytes = Book(new[] { "Store", "Employee ID", "Name", "Job Title", "Payroll Group" },
            new[] { "1480001|Merghany", "100", "Ali", "Crew", "Manfoods Company" },
            new[] { "1480001 | Merghany", "101", "Sara", "Crew", "Hourly Paid" },
            new[] { "1480001 | Merghany", "100", "Ali", "Crew", "Manfoods Company" },   // repeated id
            new[] { "9999 | Unknown", "102", "Omar", "Crew", "Manfoods Company" },
            new[] { "", "103", "Mona", "Crew", "Manfoods Company" });                   // no store

        var p = UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store", "no group");

        Assert.Equal(4, p.Rows.Count);
        Assert.Equal(1, p.Duplicates);
        Assert.Equal(1, p.MissingStore);
        Assert.Equal(2, p.Rows.Count(r => r.StoreName == "1480001 | Merghany"));
        Assert.Contains("9999 | Unknown", p.UnknownStores);
        Assert.Equal("Hourly Paid", p.Rows.Single(r => r.EmployeeId == "101").PayrollGroup);
    }

    [Fact]
    public void ArabicHeaders_AreRecognised()
    {
        var bytes = Book(new[] { "الرقم الوظيفي", "الاسم", "الوظيفة", "مجموعة الرواتب", "المطعم" }, new[] { "100", "Ali", "Crew", "Hourly Paid", "1480001 | Merghany" });
        var p = UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store", "no group");
        var row = Assert.Single(p.Rows);
        Assert.Equal("100", row.EmployeeId); Assert.Equal("Hourly Paid", row.PayrollGroup);
    }

    [Fact]
    public void MissingEmployeeIdColumn_IsRejected()
    {
        var bytes = Book(new[] { "Name", "Store", "Payroll Group" }, new[] { "Ali", "1480001 | Merghany", "Hourly Paid" });
        var ex = Assert.Throws<InvalidOperationException>(() => UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store", "no group"));
        Assert.Equal("no id", ex.Message);
    }

    [Fact]
    public void MissingPayrollGroupColumn_IsRejected()
    {
        var bytes = Book(new[] { "Employee ID", "Store" }, new[] { "100", "1480001 | Merghany" });
        var ex = Assert.Throws<InvalidOperationException>(() => UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store", "no group"));
        Assert.Equal("no group", ex.Message);
    }
}

/// <summary>The Crew Trainers page: trainers against the plan, new hires to train, and list movement.</summary>
public class CrewTrainerServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CrewTrainerService NewService(AppDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new CrewTrainerService(db, new WorkforcePlanningService(db, new StoreAccessService(db), cache), cache);
    }

    private static void Person(AppDbContext db, int month, string id, string store, string job = "Crew", DateOnly? hired = null) =>
        db.ActiveEmployees.Add(new ActiveEmployee { Year = 2026, Month = month, EmployeeId = id, Name = "N" + id, Store = store, JobTitle = job, HireDate = hired });

    private static void Trainer(AppDbContext db, int month, string id, string store) =>
        db.CrewTrainerEmployees.Add(new CrewTrainerEmployee { Year = 2026, Month = month, EmployeeId = id, Name = "N" + id, StoreName = store, PayrollGroup = "Manfoods Company" });

    [Fact]
    public async Task NoLists_HasNoData()
    {
        var dto = await NewService(NewDb()).GetAsync(null, null, null, "Admin", null);
        Assert.False(dto.HasData);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(3, 1)] [InlineData(6, 1)] [InlineData(8, 1)]
    [InlineData(9, 2)] [InlineData(14, 2)] [InlineData(15, 3)] [InlineData(17, 3)]
    public void RequiredTrainers_IsCrewLevelDividedBySix_RoundedHalfUp(int crew, int expected) =>
        Assert.Equal(expected, CrewTrainerService.RequiredTrainers(crew));

    [Fact]
    public async Task Store_ComparesTrainersToTheProjectionAndToTheOnePerSixRule()
    {
        var db = NewDb();
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "1 | A", JobTitle = "Crew Trainer", ProjectedHeadcount = 3 });
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "2 | B", JobTitle = "Crew Trainer", ProjectedHeadcount = 1 });
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "2 | B", JobTitle = "Crew", ProjectedHeadcount = 7 });
        // Crew level = Crew and Hourly Paid Crew (the trainer jobs are flagged too, but never counted as the people trained).
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "Crew", PayrollGroup = "Manfoods Company", IsCrewLevel = true });
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "Hourly Paid Crew", PayrollGroup = "Hourly Paid", IsCrewLevel = true });
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "Crew Trainer", PayrollGroup = "Manfoods Company", IsCrewLevel = true });
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "Hostess", PayrollGroup = "Manfoods Company", IsCrewLevel = false });
        // Store A: 16 Crew on the roster, 2 of them are trainers -> 14 crew level, rule asks for round(14/6) = 2.
        for (int i = 1; i <= 16; i++) Person(db, 2, "a" + i, "1 | A");
        Person(db, 2, "h1", "1 | A", "Hostess");   // not crew level
        Trainer(db, 2, "a1", "1 | A"); Trainer(db, 2, "a2", "1 | A");
        // Store B: 7 crew level (hourly paid included), no trainer -> rule asks for round(7/6) = 1.
        for (int i = 1; i <= 4; i++) Person(db, 2, "b" + i, "2 | B");
        for (int i = 5; i <= 7; i++) Person(db, 2, "b" + i, "2 | B", "Hourly Paid Crew");
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 2, null, "Admin", null);

        Assert.True(dto.HasCrewLevelJobs);
        var a = Assert.Single(dto.ByStore, r => r.Store == "1 | A");
        Assert.Equal(3, a.Projected); Assert.Equal(2, a.Actual); Assert.Equal(-1, a.Gap);       // against the projection: one short
        Assert.Equal(14, a.CrewLevel); Assert.Equal(2, a.Required); Assert.Equal(0, a.GapRule);  // against the rule: just right
        var b = Assert.Single(dto.ByStore, r => r.Store == "2 | B");
        Assert.Equal(7, b.CrewLevel); Assert.Equal(1, b.Required); Assert.Equal(0, b.Actual); Assert.Equal(-1, b.GapRule);
        Assert.Equal("2 | B", dto.ByStore[0].Store);                                             // the store most short of trainers (by the rule) comes first
        Assert.Equal(21, dto.Kpis.CrewLevel); Assert.Equal(3, dto.Kpis.Required); Assert.Equal(2, dto.Kpis.Actual); Assert.Equal(-1, dto.Kpis.GapRule);
    }

    [Fact]
    public async Task LeaderRollUps_AddUpTheStoreRows_AndSkipBlankLeaders()
    {
        var db = NewDb();
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "1 | A", JobTitle = "Crew Trainer", ProjectedHeadcount = 3 });
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "2 | B", JobTitle = "Crew Trainer", ProjectedHeadcount = 1 });
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "3 | C", JobTitle = "Crew Trainer", ProjectedHeadcount = 2 });
        db.JobPayrollGroups.Add(new JobPayrollGroup { JobTitle = "Crew", PayrollGroup = "Manfoods Company", IsCrewLevel = true });
        for (int i = 1; i <= 12; i++) Person(db, 2, "a" + i, "1 | A");
        Trainer(db, 2, "a1", "1 | A");
        for (int i = 1; i <= 6; i++) Person(db, 2, "b" + i, "2 | B");
        Trainer(db, 2, "b1", "2 | B");
        Person(db, 2, "c1", "3 | C");
        db.StoreReferences.AddRange(
            new StoreReference { Year = 2026, Month = 2, StoreName = "1 | A", OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
            new StoreReference { Year = 2026, Month = 2, StoreName = "2 | B", OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" });
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 2, null, "Admin", null);

        var amy = Assert.Single(dto.ByOperationConsultant);                                       // store C has no leader: skipped
        Assert.Equal("Amy", amy.Name); Assert.Equal(2, amy.StoreCount);
        Assert.Equal(4, amy.Projected); Assert.Equal(2, amy.Actual); Assert.Equal(-2, amy.Gap);
        Assert.Equal(16, amy.CrewLevel); Assert.Equal(3, amy.Required); Assert.Equal(-1, amy.GapRule);
        Assert.Equal("Mona", Assert.Single(dto.ByOperationManager).Name);
        Assert.Equal("Dan", Assert.Single(dto.ByOperationDirector).Name);
        Assert.Equal("Sam", Assert.Single(dto.BySeniorOperationConsultant).Name);
        Assert.Equal(3, dto.ByStore.Count);
    }

    [Fact]
    public async Task NoCrewLevelJobs_MeansTheRuleCannotBeApplied()
    {
        var db = NewDb();
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "1 | A", JobTitle = "Crew Trainer", ProjectedHeadcount = 1 });
        Person(db, 2, "1", "1 | A"); Trainer(db, 2, "1", "1 | A");
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 2, null, "Admin", null);

        Assert.False(dto.HasCrewLevelJobs);
        Assert.Equal(0, Assert.Single(dto.ByStore).Required);
    }

    [Fact]
    public async Task Movement_ShowsWhoJoinedAndLeft_AndWhyTheyLeft()
    {
        var db = NewDb();
        db.JobHeadcountProjections.Add(new JobHeadcountProjection { Year = 2026, Month = 2, StoreName = "1 | A", JobTitle = "Crew Trainer", ProjectedHeadcount = 3 });
        foreach (var id in new[] { "1", "2", "3", "4" }) Person(db, 2, id, "1 | A");   // everyone but "5" is on the February roster
        Person(db, 1, "1", "1 | A");                                                      // January has a roster too
        Trainer(db, 1, "1", "1 | A"); Trainer(db, 1, "2", "1 | A"); Trainer(db, 1, "3", "1 | A"); Trainer(db, 1, "5", "1 | A");
        Trainer(db, 2, "1", "1 | A"); Trainer(db, 2, "4", "1 | A");                     // 4 joined; 2, 3, 5 left
        db.Resignations.Add(new Resignation { Year = 2026, Month = 2, EmployeeId = "2", Store = "1 | A", JobTitle = "Crew" });
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 2, null, "Admin", null);

        Assert.True(dto.HasPrevious);
        Assert.Equal(1, dto.PreviousMonth);
        Assert.Equal("4", Assert.Single(dto.Entered).EmployeeId);
        Assert.Equal("resigned", dto.Left.Single(m => m.EmployeeId == "2").Status);
        Assert.Equal("active", dto.Left.Single(m => m.EmployeeId == "3").Status);   // still on the roster, off the list
        Assert.Equal("gone", dto.Left.Single(m => m.EmployeeId == "5").Status);     // not on the roster any more
        Assert.Equal(1, dto.Kpis.Resigned);
    }
}

/// <summary>Reading the job → payroll group reference list.</summary>
public class JobPayrollGroupUploadParseTests
{
    private static byte[] Book(string[] headers, params string[][] rows)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Reads_ByHeader_KeepsTheLastRepeat_AndSkipsBlanks()
    {
        var bytes = Book(new[] { "Payroll Group", "Job Title" },
            new[] { "Manfoods Company", "Crew" },
            new[] { "Hourly Paid", "hourly paid crew trainer" },
            new[] { "Hourly Paid", "  CREW " },        // repeat of Crew (case / spacing ignored): the last one wins
            new[] { "", "Barista" },                     // no group
            new[] { "Hourly Paid", "" });                // no job

        var p = UploadService.ParseJobPayrollGroups(bytes, "no job", "no group");

        Assert.Equal(2, p.Rows.Count);
        Assert.Equal(1, p.Duplicates);
        Assert.Equal(2, p.Blank);
        Assert.Equal("Hourly Paid", p.Rows.Single(r => r.JobTitle == "CREW").PayrollGroup);
    }

    [Fact]
    public void CrewLevelColumn_IsReadWhenPresent_AndFlaggedAsMissingWhenNot()
    {
        var withCol = Book(new[] { "Job Title", "Payroll Group", "crew level" },
            new[] { "Crew", "Manfoods Company", "TRUE" }, new[] { "Barista", "Manfoods Company", "" }, new[] { "Hourly Paid Crew", "Hourly Paid", "yes" });
        var p = UploadService.ParseJobPayrollGroups(withCol, "no job", "no group");
        Assert.True(p.HasCrewLevelColumn);
        Assert.Equal(new[] { "Crew", "Hourly Paid Crew" }, p.Rows.Where(r => r.IsCrewLevel).Select(r => r.JobTitle).ToArray());

        var without = Book(new[] { "Job Title", "Payroll Group" }, new[] { "Crew", "Manfoods Company" });
        Assert.False(UploadService.ParseJobPayrollGroups(without, "no job", "no group").HasCrewLevelColumn);
    }

    [Fact]
    public void MissingColumns_AreRejected()
    {
        var noGroup = Book(new[] { "Job Title" }, new[] { "Crew" });
        Assert.Equal("no group", Assert.Throws<InvalidOperationException>(() => UploadService.ParseJobPayrollGroups(noGroup, "no job", "no group")).Message);
        var noJob = Book(new[] { "Payroll Group" }, new[] { "Hourly Paid" });
        Assert.Equal("no job", Assert.Throws<InvalidOperationException>(() => UploadService.ParseJobPayrollGroups(noJob, "no job", "no group")).Message);
    }
}
