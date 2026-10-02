namespace MvcApp.Services;

/// <summary>
/// Maximum lengths for free-text values users submit, enforced where the request is validated
/// (the database columns are NVARCHAR(MAX), so no schema change is involved). A value over its limit
/// is rejected with a validation message — it is never silently cut. The limits are well above
/// real use: a note is a few sentences, a close reason a short explanation, a name a few words.
/// </summary>
public static class InputLimits
{
    /// <summary>An Action Plan note (a few paragraphs at most).</summary>
    public const int NoteText = 4000;
    /// <summary>The reason an Admin gives when closing an Action Plan by hand.</summary>
    public const int CloseReason = 2000;
    /// <summary>A person's display name (Assigned Name / plan owner).</summary>
    public const int PersonName = 200;
    /// <summary>One language's text of an editable recommendation template.</summary>
    public const int RecommendationText = 2000;
    /// <summary>Email address (the RFC maximum; the column is 450).</summary>
    public const int Email = 254;
    public const int Phone = 30;
    /// <summary>User-Agent request header stored in login history. Real browsers send well under 500
    /// characters; the header is client-controlled, so it is clipped rather than refused (a login must
    /// not fail because of a header, and it is not the user's own data).</summary>
    public const int UserAgent = 1024;

    public static bool Exceeds(string? value, int max) => value != null && value.Length > max;

    /// <summary>Clips a client-supplied header value to <paramref name="max"/> characters.</summary>
    public static string? Clip(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];
}
