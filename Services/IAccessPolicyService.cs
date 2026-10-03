namespace MvcApp.Services;

/// <summary>
/// Reads and writes the per-area access configuration (page_access_config).
/// A missing row means "restricted" so the default behaviour is unchanged
/// until an Admin opens an area.
/// </summary>
public interface IAccessPolicyService
{
    /// <summary>Whether the given area is restricted (true = restricted-roles see
    /// only their own stores; false = they see everything there). Unknown/missing
    /// areas are treated as restricted.</summary>
    Task<bool> IsRestrictedAsync(string areaKey);

    /// <summary>True if at least one area is open (not restricted) — used to widen
    /// the shared filter-dropdown endpoints so an open page's dropdowns work.</summary>
    Task<bool> AnyOpenAsync();

    /// <summary>The full area → is-restricted map for the Settings page (every
    /// known area present, defaulting to restricted).</summary>
    Task<Dictionary<string, bool>> GetAllAsync();

    /// <summary>Persists the given area → is-restricted settings. Only known areas
    /// are written; unknown keys are ignored. Returns one line per area whose value actually changed
    /// (for the Activity Log), empty when nothing changed.</summary>
    Task<List<string>> SaveAsync(Dictionary<string, bool> settings, string? adminName);
}
