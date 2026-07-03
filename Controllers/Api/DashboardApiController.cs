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
    private readonly IGeminiService _gemini;

    public DashboardApiController(IDashboardService dashboard, IStoreService stores, IGeminiService gemini)
    {
        _dashboard = dashboard;
        _stores = stores;
        _gemini = gemini;
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

    [HttpGet("store-comparison")]
    public async Task<IActionResult> StoreComparison([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName);
        return Ok(await _dashboard.GetStoreComparisonAsync(kpis.Month, kpis.Year, role, assignedName));
    }

    [HttpGet("oc-om-analysis")]
    public async Task<IActionResult> OcOmAnalysis([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName);
        return Ok(await _dashboard.GetOcOmAnalysisAsync(kpis.Month, kpis.Year, role, assignedName));
    }

    [HttpGet("smart-insights")]
    public async Task<IActionResult> SmartInsights([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName);
        return Ok(await _dashboard.GetSmartInsightsAsync(kpis.Month, kpis.Year, role, assignedName));
    }

    [HttpGet("trend-matrix")]
    public async Task<IActionResult> TrendMatrix()
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTrendMatrixAsync(role, assignedName));
    }

    // Role system simplified to Admin/User — no more per-store restriction.
    private Task<bool> CanAccessStoreAsync(string store, string role, string? assignedName) =>
        Task.FromResult(true);

    [HttpGet("store-employees")]
    public async Task<IActionResult> StoreEmployees([FromQuery] string store, [FromQuery] int? month, [FromQuery] int? year)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest();
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        if (!await CanAccessStoreAsync(store, role, assignedName)) return Forbid();
        return Ok(await _dashboard.GetStoreEmployeesAsync(store, month, year));
    }

    [HttpGet("store-resignations")]
    public async Task<IActionResult> StoreResignations([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest();
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        if (!await CanAccessStoreAsync(store, role, assignedName)) return Forbid();
        return Ok(await _dashboard.GetStoreResignationHistoryAsync(store));
    }

    public record AiChatRequest(string Question, int? Month, int? Year, string? Store);

    [HttpPost("ai-chat")]
    public async Task<IActionResult> AiChat([FromBody] AiChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "السؤال لا يمكن أن يكون فارغًا." });

        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();

        var kpis = await _dashboard.GetKpisAsync(request.Month, request.Year, request.Store, role, assignedName);

        // Run sequentially — EF Core DbContext does not support concurrent queries
        var jobTitleData = await _dashboard.GetTurnoverByJobTitleAsync(kpis.Month, kpis.Year, request.Store, role, assignedName);
        var tenureData   = await _dashboard.GetTurnoverByTenureAsync(kpis.Month, kpis.Year, request.Store, role, assignedName);
        var genderData   = await _dashboard.GetGenderBreakdownAsync(kpis.Month, kpis.Year, request.Store, role, assignedName);

        // Per-store breakdown only when viewing all stores
        var storeData = string.IsNullOrEmpty(request.Store)
            ? await _dashboard.GetPerStoreTurnoverAsync(kpis.Month, kpis.Year, role, assignedName)
            : new List<StoreBreakdown>();

        var context = new GeminiContext
        {
            Month             = kpis.Month,
            Year              = kpis.Year,
            Store             = request.Store,
            TotalHeadcount    = kpis.TotalHeadcount,
            TotalResignations = kpis.TotalResignations,
            TurnoverRate      = kpis.TurnoverRate,
            NewHires          = kpis.NewHires,
            StoreBreakdowns    = storeData,
            TurnoverByJobTitle = jobTitleData.Select(x => (x.Label, x.Value)).ToList(),
            TurnoverByTenure   = tenureData.Select(x => (x.Label, x.Value)).ToList(),
            GenderBreakdown    = genderData.Select(x => (x.Label, x.Value)).ToList()
        };

        var answer = await _gemini.AskAsync(request.Question, context);
        return Ok(new { answer });
    }
}
