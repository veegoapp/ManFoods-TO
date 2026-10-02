namespace MvcApp.Services;

/// <summary>Display time for the Activity Logs: always Africa/Cairo, whatever the server or the viewer's device
/// uses. Storage stays UTC. Resolves the zone by its IANA id and falls back to the Windows id (IIS hosting).</summary>
public static class CairoTime
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(() =>
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        }
        // No tz database on the host: Egypt is UTC+2 (+3 in summer when DST applies); fall back to the standard offset.
        return TimeZoneInfo.CreateCustomTimeZone("Africa/Cairo", TimeSpan.FromHours(2), "Cairo", "Cairo");
    });

    public static TimeZoneInfo TimeZone => Zone.Value;

    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone.Value);

    /// <summary>"2026-10-03 00:15:07" in Cairo time.</summary>
    public static string Format(DateTime utc) => FromUtc(utc).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Start of a Cairo calendar day, as a UTC instant.</summary>
    public static DateTime DayStartUtc(DateOnly day) => ToUtc(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>The last instant of a Cairo calendar day, as a UTC instant.</summary>
    public static DateTime DayEndUtc(DateOnly day) => ToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue)).AddTicks(-1);

    private static DateTime ToUtc(DateTime cairoLocal)
    {
        var unspecified = DateTime.SpecifyKind(cairoLocal, DateTimeKind.Unspecified);
        // An hour that does not exist (DST gap) is moved forward rather than throwing.
        if (Zone.Value.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone.Value);
    }
}
