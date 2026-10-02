using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>
/// Globally-registered filter that rejects an out-of-range month/year, an over-long period range,
/// or an oversized store/OM/OC/job filter list with a normal 400 validation response — before the
/// request reaches a service, instead of surfacing as a 500 (or an enormous SQL "IN (...)" list).
/// Looks arguments up by their conventional names (see <see cref="PeriodLimits"/>); actions without
/// such arguments are untouched. Runs after the authentication filters (Order 100) so an
/// unauthenticated caller still gets the normal redirect, not a validation answer.
/// </summary>
public sealed class ReportParameterValidationFilter : IActionFilter, IOrderedFilter
{
    public int Order => 100;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var args = context.ActionArguments;
        var parameters = context.ActionDescriptor.Parameters;
        if (args.Count == 0 && parameters.Count == 0) return;

        // The Excel export actions take plain int month/year that default to 0 = "not given";
        // JSON endpoints (ApiController) never do.
        var isApi = context.ActionDescriptor.EndpointMetadata.OfType<ApiControllerAttribute>().Any();

        // A required (non-nullable) int that the request left out is not in ActionArguments, yet the action
        // still runs with its default, 0 — so look at it as 0 rather than not at all.
        object? Get(string name)
        {
            if (args.TryGetValue(name, out var value)) return value;
            return parameters.Any(p => p.Name == name && p.ParameterType == typeof(int)) ? 0 : null;
        }

        var error = PeriodLimits.Validate(Get, zeroMeansNotProvided: !isApi, DateTime.Now);
        if (error is not { } e) return;

        context.Result = new BadRequestObjectResult(new ValidationProblemDetails(
            new Dictionary<string, string[]> { [e.Parameter] = new[] { e.Message } }));
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
