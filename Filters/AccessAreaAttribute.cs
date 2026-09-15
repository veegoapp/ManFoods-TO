using Microsoft.AspNetCore.Mvc.Filters;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>
/// Declares which access area an API controller or action serves. A global
/// filter (AccessAreaFilter) reads this and stores it in the per-request
/// IAccessAreaContext, which StoreAccessService consults to decide whether a
/// restricted role's view should be widened for that area. Action-level
/// attributes override controller-level ones.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AccessAreaAttribute : Attribute
{
    public string Area { get; }
    public AccessAreaAttribute(string area) => Area = area;
}

/// <summary>Globally-registered filter that copies the endpoint's
/// [AccessArea] into the scoped IAccessAreaContext before the action runs.</summary>
public sealed class AccessAreaFilter : IActionFilter
{
    private readonly IAccessAreaContext _context;

    public AccessAreaFilter(IAccessAreaContext context) => _context = context;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        // Action-level attribute wins over controller-level.
        var attr = context.ActionDescriptor.EndpointMetadata
            .OfType<AccessAreaAttribute>()
            .LastOrDefault();
        if (attr != null) _context.Area = attr.Area;
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
