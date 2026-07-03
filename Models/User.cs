using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

[Table("users")]
public class User
{
    [Column("id")]
    public int Id { get; set; }

    [Column("email")]
    [Required]
    public string Email { get; set; } = "";

    [Column("phone")]
    [Required]
    public string Phone { get; set; } = "";

    // Null until the account is activated. Bulk-created accounts start
    // pending: no password, can't log in, until the OTP flow sets one.
    [Column("password_hash")]
    public string? PasswordHash { get; set; }

    [Column("role")]
    [Required]
    public string Role { get; set; } = "";

    [Column("assigned_name")]
    public string? AssignedName { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
