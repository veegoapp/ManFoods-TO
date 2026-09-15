using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Admin-configurable per-area access setting. One row per access area
/// (see Services/AccessAreas). IsRestricted = true means restricted roles
/// (OC/OM/Head Manager/…) see only their own stores in that area; false means
/// they see everything there (like the User role). A missing row defaults to
/// restricted, so the out-of-the-box behaviour is unchanged until an Admin
/// opens an area on the Settings page.
/// </summary>
[Table("page_access_config")]
public class PageAccessConfig
{
    [Column("area_key")]
    public string AreaKey { get; set; } = "";

    [Column("is_restricted")]
    public bool IsRestricted { get; set; } = true;

    [Column("updated_by_name")]
    public string? UpdatedByName { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
