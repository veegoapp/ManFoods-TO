using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Whether a page is hidden from one role of the User interface (Home area): its sidebar link disappears, opening it
/// redirects to another page and the page's APIs are refused. One row per page and role. A missing row means the
/// page's default applies (visible, unless it is still under review — see UserPages.HiddenByDefault).
/// Set by an Admin in Settings → Pages.
/// </summary>
[Table("page_visibility")]
public class PageVisibility
{
    [Column("page_key")]
    public string PageKey { get; set; } = "";

    /// <summary>The role the setting applies to (one of UserPages.Roles).</summary>
    [Column("role")]
    public string Role { get; set; } = "";

    [Column("is_hidden")]
    public bool IsHidden { get; set; }

    [Column("updated_by_name")]
    public string? UpdatedByName { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
