using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>Small key/value store for app-wide config that doesn't belong on
/// any single entity — currently just the admin recovery key hash.</summary>
[Table("app_settings")]
public class AppSetting
{
    [Key]
    [Column("key")]
    public string Key { get; set; } = "";

    [Column("value")]
    public string Value { get; set; } = "";
}
