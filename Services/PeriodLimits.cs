namespace MvcApp.Services;

/// <summary>
/// The one definition of which month/year/range values a report request may carry, shared by
/// the request filter (which turns a bad value into a normal 400 response) and
/// <see cref="DashboardService.ExpandRangeKeys"/> (which refuses to build an oversized list of
/// period keys even if a caller bypasses the filter). Without it a request like
/// fromYear=1&amp;toYear=9999 expanded to ~120,000 keys and became a SQL "IN (...)" list.
///
/// The limits follow how the application is used: periods come from monthly uploads whose year
/// is already constrained to 2000–2100 (Range on every upload form), go-live is 2026-01, and the
/// pages only ever offer periods that have been uploaded — a handful of years at most. The widest
/// range any screen requests is "first uploaded period → latest", so 120 months (10 years) leaves
/// ample headroom while bounding every key list to 120 entries.
/// </summary>
public static class PeriodLimits
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const int MaxRangeMonths = 120;
    /// <summary>A "months" list is a subset of Jan–Dec, so more than 12 entries is never legitimate.</summary>
    public const int MaxMonthsListEntries = 12;
    /// <summary>Longest comma-separated store/OM/OC/job filter accepted (the 8 KB URL limit already caps
    /// real requests far below this); store names are at most 450 characters (database column width).</summary>
    public const int MaxFilterValues = 1000;
    public const int MaxFilterValueLength = 450;

    private static readonly string[] MonthParameters = { "month", "fromMonth", "toMonth" };
    private static readonly string[] YearParameters = { "year", "fromYear", "toYear", "sinceYear", "yearB" };
    private static readonly string[] MonthListParameters = { "months", "monthsB" };
    private static readonly string[] FilterListParameters =
        { "store", "om", "oc", "soc", "od", "jobs", "storeB", "omB", "ocB", "socB", "odB" };

    public static bool IsValidMonth(int month) => month is >= 1 and <= 12;
    public static bool IsValidYear(int year) => year is >= MinYear and <= MaxYear;

    /// <summary>Number of months from the earlier to the later of the two periods, both included.</summary>
    public static int SpanInMonths(int fromMonth, int fromYear, int toMonth, int toYear) =>
        Math.Abs((toYear * 12 + toMonth) - (fromYear * 12 + fromMonth)) + 1;

    /// <summary>
    /// Checks the period- and filter-related values of a request. <paramref name="get"/> returns the
    /// bound value of an action argument (null when the action has no such argument or it is absent).
    /// With <paramref name="zeroMeansNotProvided"/> (the Excel export actions, whose plain int
    /// month/year default to 0) a month/year of 0 is allowed. Returns null when everything is fine.
    /// </summary>
    public static (string Parameter, string Message)? Validate(Func<string, object?> get, bool zeroMeansNotProvided, DateTime now)
    {
        foreach (var name in MonthParameters)
            if (get(name) is int m && !(zeroMeansNotProvided && name == "month" && m == 0) && !IsValidMonth(m))
                return (name, $"'{name}' must be a month number from 1 to 12.");

        foreach (var name in YearParameters)
            if (get(name) is int y && !(zeroMeansNotProvided && name == "year" && y == 0) && !IsValidYear(y))
                return (name, $"'{name}' must be a year between {MinYear} and {MaxYear}.");

        foreach (var name in MonthListParameters)
            if (get(name) is string list && ValidateMonthList(list) is { } listError)
                return (name, $"'{name}' {listError}");

        foreach (var name in FilterListParameters)
            if (get(name) is string values && ValidateFilterList(values) is { } filterError)
                return (name, $"'{name}' {filterError}");

        // Range: mirror how DashboardService.ResolvePeriods fills in a missing end.
        var fromMonth = get("fromMonth") as int?;
        var fromYear = get("fromYear") as int?;
        if (fromMonth != null || fromYear != null)
        {
            var toMonth = (get("toMonth") as int?) ?? (get("month") as int?);
            var toYear = (get("toYear") as int?) ?? (get("year") as int?);
            if (toMonth == 0) toMonth = null;
            if (toYear == 0) toYear = null;
            var tm = toMonth ?? now.Month;
            var ty = toYear ?? now.Year;
            var fm = fromMonth ?? tm;
            var fy = fromYear ?? ty;
            if (SpanInMonths(fm, fy, tm, ty) > MaxRangeMonths)
                return ("fromYear", $"The requested period range is longer than {MaxRangeMonths} months.");
        }

        return null;
    }

    private static string? ValidateMonthList(string list)
    {
        if (string.IsNullOrWhiteSpace(list)) return null;
        var tokens = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > MaxMonthsListEntries) return $"may list at most {MaxMonthsListEntries} months.";
        foreach (var token in tokens)
            if (!int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var m) || !IsValidMonth(m))
                return "must be a comma-separated list of month numbers from 1 to 12.";
        return null;
    }

    private static string? ValidateFilterList(string values)
    {
        if (string.IsNullOrWhiteSpace(values)) return null;
        var tokens = values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > MaxFilterValues) return $"may list at most {MaxFilterValues} values.";
        if (tokens.Any(t => t.Length > MaxFilterValueLength)) return $"has a value longer than {MaxFilterValueLength} characters.";
        return null;
    }
}
