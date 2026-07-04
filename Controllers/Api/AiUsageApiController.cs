using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/ai-usage")]
[RequireAdminAuth]
public class AiUsageApiController : ControllerBase
{
    private readonly IAiUsageService _usage;

    public AiUsageApiController(IAiUsageService usage) => _usage = usage;

    [HttpGet("limit")]
    public async Task<IActionResult> GetLimit() => Ok(new { limit = await _usage.GetDailyLimitAsync() });

    public record SetLimitRequest(int Limit);

    [HttpPost("limit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLimit([FromBody] SetLimitRequest request)
    {
        if (request.Limit < 1) return BadRequest(new { error = "الحد لازم يكون رقم أكبر من صفر." });
        await _usage.SetDailyLimitAsync(request.Limit);
        return Ok(new { limit = request.Limit });
    }

    [HttpGet("cost-rates")]
    public async Task<IActionResult> GetCostRates() => Ok(await _usage.GetCostRatesAsync());

    public record SetCostRatesRequest(double InputPricePerMillion, double OutputPricePerMillion);

    [HttpPost("cost-rates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCostRates([FromBody] SetCostRatesRequest request)
    {
        if (request.InputPricePerMillion < 0 || request.OutputPricePerMillion < 0)
            return BadRequest(new { error = "الأسعار لازم تكون صفر أو أكبر." });
        await _usage.SetCostRatesAsync(request.InputPricePerMillion, request.OutputPricePerMillion);
        return Ok(await _usage.GetCostRatesAsync());
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(await _usage.GetSummaryAsync());

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] int days = 30) => Ok(await _usage.GetTrendAsync(Math.Clamp(days, 1, 90)));

    [HttpGet("top-users")]
    public async Task<IActionResult> TopUsers([FromQuery] int days = 30) => Ok(await _usage.GetTopUsersAsync(Math.Clamp(days, 1, 90)));

    [HttpPost("reset/{userId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUser(int userId)
    {
        await _usage.ResetUserTodayAsync(userId);
        return Ok();
    }
}
