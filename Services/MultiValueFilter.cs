namespace MvcApp.Services;

/// <summary>
/// Store/OM/OC filter parameters stay <c>string?</c> everywhere (same contract every
/// existing single-value caller already uses) but are read as a comma-separated list,
/// mirroring the "months" convention already used by DashboardService.ResolvePeriods.
/// </summary>
internal static class MultiValueFilter
{
    public static List<string>? Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
