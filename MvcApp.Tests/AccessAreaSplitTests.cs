using Xunit;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Controllers.Api;
using MvcApp.Data;
using MvcApp.Filters;
using MvcApp.Models;
using MvcApp.Services;

namespace MvcApp.Tests;

/// <summary>Hiring Forecast and Crew Trainers have their own access areas (they used to share Workforce Planning's).</summary>
public class AccessAreaSplitTests
{
    private static (AppDbContext Db, AccessPolicyService Policy) New()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new AccessPolicyService(db, new MemoryCache(new MemoryCacheOptions())));
    }

    private static string? AreaOf(string action) =>
        typeof(WorkforcePlanningApiController).GetMethod(action)!.GetCustomAttributes<AccessAreaAttribute>().SingleOrDefault()?.Area;

    [Fact]
    public void TheTwoEndpoints_RunOnTheirOwnAreas_AndWorkforcePlanningKeepsTheRest()
    {
        Assert.Equal(AccessAreas.HiringForecast, AreaOf(nameof(WorkforcePlanningApiController.HiringForecast)));
        Assert.Equal(AccessAreas.CrewTrainers, AreaOf(nameof(WorkforcePlanningApiController.CrewTrainers)));
        Assert.Null(AreaOf(nameof(WorkforcePlanningApiController.Summary)));
        Assert.Equal(AccessAreas.WorkforcePlanning, typeof(WorkforcePlanningApiController).GetCustomAttribute<AccessAreaAttribute>()!.Area);
        Assert.Contains(AccessAreas.HiringForecast, AccessAreas.All);
        Assert.Contains(AccessAreas.CrewTrainers, AccessAreas.All);
    }

    [Fact]
    public async Task NoRows_EverythingIsRestricted()
    {
        var (_, policy) = New();
        Assert.True(await policy.IsRestrictedAsync(AccessAreas.HiringForecast));
        Assert.True(await policy.IsRestrictedAsync(AccessAreas.CrewTrainers));
    }

    [Fact]
    public async Task BeforeTheFirstSave_TheNewAreasFollowWorkforcePlanning()
    {
        var (db, policy) = New();
        db.PageAccessConfigs.Add(new PageAccessConfig { AreaKey = AccessAreas.WorkforcePlanning, IsRestricted = false, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        Assert.False(await policy.IsRestrictedAsync(AccessAreas.HiringForecast));
        var all = await policy.GetAllAsync();
        Assert.False(all[AccessAreas.CrewTrainers]);
        Assert.True(all[AccessAreas.Retention]);   // unrelated areas are untouched
    }

    [Fact]
    public async Task OnceSaved_EachAreaIsIndependent()
    {
        var (db, policy) = New();
        db.PageAccessConfigs.Add(new PageAccessConfig { AreaKey = AccessAreas.WorkforcePlanning, IsRestricted = false, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await policy.SaveAsync(new Dictionary<string, bool>
        {
            [AccessAreas.WorkforcePlanning] = false, [AccessAreas.HiringForecast] = true, [AccessAreas.CrewTrainers] = false,
        }, "admin");

        Assert.False(await policy.IsRestrictedAsync(AccessAreas.WorkforcePlanning));
        Assert.True(await policy.IsRestrictedAsync(AccessAreas.HiringForecast));
        Assert.False(await policy.IsRestrictedAsync(AccessAreas.CrewTrainers));
    }
}
