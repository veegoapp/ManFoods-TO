using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Whether a page is hidden from the User interface (Home area): its sidebar link disappears and opening it
/// redirects to another page. A missing row means the page is visible. Set by an Admin in Settings → Pages.
/// </summary>
[Table("page_visibility")]
public class PageVisibility
{
    [Column("page_key")]
    public string PageKey { get; set; } = "";

    [Column("is_hidden")]
    public bool IsHidden { get; set; }

    [Column("updated_by_name")]
    public string? UpdatedByName { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
