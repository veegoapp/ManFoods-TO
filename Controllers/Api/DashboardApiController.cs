using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/dashboard")]
[RequireAuth]
public class DashboardApiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IStoreService _stores;

    public DashboardApiController(IDashboardService dashboard, IStoreService stores)
    {
        _dashboard = dashboard;
        _stores = stores;
    }

    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var result = await _dashboard.GetKpisAsync(month, year, store, role, assignedName);
        return Ok(result);
    }

    [HttpGet("turnover-by-job-title")]
    public async Task<IActionResult> TurnoverByJobTitle([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTurnoverByJobTitleAsync(month, year, store, role, assignedName));
    }

    [HttpGet("turnover-by-tenure")]
    public async Task<IActionResult> TurnoverByTenure([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTurnoverByTenureAsync(month, year, store, role, assignedName));
    }

    [HttpGet("gender-breakdown")]
    public async Task<IActionResult> GenderBreakdown([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetGenderBreakdownAsync(month, year, store, role, assignedName));
    }

    [HttpGet("available-periods")]
    public async Task<IActionResult> AvailablePeriods()
    {
        return Ok(await _dashboard.GetAvailablePeriodsAsync());
    }

    [HttpGet("stores")]
    public async Task<IActionResult> Stores([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var stores = await _stores.GetStoresAsync(month, year, role, assignedName);
        return Ok(stores.Select(s => new { storeName = s.StoreName }));
    }
}
