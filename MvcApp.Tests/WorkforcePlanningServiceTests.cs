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
    public async Task TrainerJobs_AreMergedIntoOneCrewTrainerJob()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew Trainer", 2); Proj(db, 1, "1 | A", "Hourly Paid Crew Trainer", 3);
        Proj(db, 1, "2 | B", "hourly paid crew trainer", 1); Proj(db, 1, "1 | A", "Crew", 10);
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        Assert.Equal(16, dto.Kpis.Projected);
        var trainer = Assert.Single(dto.ByJob, r => r.Name == "Crew Trainer");
        Assert.Equal(6, trainer.Projected);
        Assert.DoesNotContain(dto.ByJob, r => r.Name.Contains("Hourly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Crew Trainer", dto.Jobs);
    }

    private static void Person(AppDbContext db, int month, string id, string store, string job, string group = "Hourly") =>
        db.ActiveEmployees.Add(new ActiveEmployee { Year = 2026, Month = month, EmployeeId = id, Store = store, JobTitle = job, PayrollGroup = group });

    private static void Trainer(AppDbContext db, int month, string id, string store) =>
        db.CrewTrainerEmployees.Add(new CrewTrainerEmployee { Year = 2026, Month = month, EmployeeId = id, StoreName = store });

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
        Assert.Equal(3, trainer.Projected);
        Assert.Equal(2, trainer.Actual);
        Assert.Equal(1, Assert.Single(jan.ByJob, r => r.Name == "Crew").Actual);    // 3 roster Crew - 2 trainers
        Assert.Equal(3, jan.Kpis.Actual);                                          // total headcount is unchanged

        var feb = await NewService(db).GetAsync(2026, 2, null, null, "Admin", null);
        Assert.Equal(0, Assert.Single(feb.ByJob, r => r.Name == "Crew Trainer").Actual); // no list uploaded for February

        var mar = await NewService(db).GetAsync(2026, 3, null, null, "Admin", null);
        Assert.False(mar.HasActual); // a trainer list alone does not make March an "actual" month
    }

    [Fact]
    public async Task CrewTrainer_PayrollGroupComesFromTheTrainersOwnGroup()
    {
        var db = NewDb();
        Proj(db, 1, "1 | A", "Crew Trainer", 3); Proj(db, 1, "1 | A", "Crew", 5);
        Person(db, 1, "1", "1 | A", "Crew", "Hourly"); Person(db, 1, "2", "1 | A", "Crew", "Monthly"); Person(db, 1, "3", "1 | A", "Crew", "Monthly");
        Person(db, 1, "4", "1 | A", "Crew", "Hourly"); Person(db, 1, "5", "1 | A", "Crew", "Hourly"); Person(db, 1, "6", "1 | A", "Crew", "Hourly");
        Trainer(db, 1, "1", "1 | A"); Trainer(db, 1, "2", "1 | A"); Trainer(db, 1, "3", "1 | A");
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetAsync(2026, 1, null, null, "Admin", null);

        var monthly = Assert.Single(dto.ByPayrollGroup, r => r.Name == "Monthly");
        Assert.Equal(3, monthly.Projected);   // Crew Trainer goes to the group most trainers are in
        Assert.Equal(3, monthly.Actual);
        Assert.Equal(5, Assert.Single(dto.ByPayrollGroup, r => r.Name == "Hourly").Projected); // Crew stays in its own group
        Assert.Equal(3, Assert.Single(dto.ByPayrollGroup, r => r.Name == "Hourly").Actual);    // 6 roster Crew - 3 trainers
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
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "Crew", PayrollGroup = "Hourly", Projected = 10, Actual = 8, ExpectedAttrition = 1.5, HiringNeed = 3.5, OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
                new() { Year = 2026, Month = 1, Store = "1 | A", Job = "GEM", Projected = 4, Actual = 4, ExpectedAttrition = 0.5, HiringNeed = 0.5, OperationConsultant = "Amy", OperationManager = "Mona", OperationDirector = "Dan", SeniorOperationConsultant = "Sam" },
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
        var bytes = Book(new[] { "Store", "Employee ID", "Name", "Job Title" },
            new[] { "1480001|Merghany", "100", "Ali", "Crew" },
            new[] { "1480001 | Merghany", "101", "Sara", "Crew" },
            new[] { "1480001 | Merghany", "100", "Ali", "Crew" },   // repeated id
            new[] { "9999 | Unknown", "102", "Omar", "Crew" },
            new[] { "", "103", "Mona", "Crew" });                   // no store

        var p = UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store");

        Assert.Equal(4, p.Rows.Count);
        Assert.Equal(1, p.Duplicates);
        Assert.Equal(1, p.MissingStore);
        Assert.Equal(2, p.Rows.Count(r => r.StoreName == "1480001 | Merghany"));
        Assert.Contains("9999 | Unknown", p.UnknownStores);
    }

    [Fact]
    public void ArabicHeaders_AreRecognised()
    {
        var bytes = Book(new[] { "الرقم الوظيفي", "الاسم", "الوظيفة", "المطعم" }, new[] { "100", "Ali", "Crew", "1480001 | Merghany" });
        var p = UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store");
        Assert.Equal("100", Assert.Single(p.Rows).EmployeeId);
    }

    [Fact]
    public void MissingEmployeeIdColumn_IsRejected()
    {
        var bytes = Book(new[] { "Name", "Store" }, new[] { "Ali", "1480001 | Merghany" });
        var ex = Assert.Throws<InvalidOperationException>(() => UploadService.ParseCrewTrainers(bytes, Known, "no id", "no store"));
        Assert.Equal("no id", ex.Message);
    }
}
