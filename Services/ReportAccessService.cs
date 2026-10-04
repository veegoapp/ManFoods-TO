using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>Decides which access area an Excel export runs under, so a report never shows more stores than the
/// page it comes from. Visibility (Settings → Pages) is checked by the controllers with
/// <see cref="ReportDefinition.IsVisible"/>.</summary>
public interface IReportAccessService
{
    /// <summary>The first of the report's areas that is restricted (Settings → Access), or null when none is: the
    /// export then keeps the Reports area, whose setting still applies on top. Running under a restricted area
    /// gives a restricted role only its own stores; Reports can narrow a report but never widen it.</summary>
    Task<string?> ResolveRestrictedAreaAsync(ReportDefinition report);
}

public sealed class ReportAccessService : IReportAccessService
{
    private readonly IAccessPolicyService _policy;

    public ReportAccessService(IAccessPolicyService policy) => _policy = policy;

    public async Task<string?> ResolveRestrictedAreaAsync(ReportDefinition report)
    {
        foreach (var area in report.Areas)
            if (await _policy.IsRestrictedAsync(area)) return area;
        return null;
    }
}
