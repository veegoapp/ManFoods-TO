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
    // pending: no password, can't log in, until "Generate Default Passwords"
    // assigns one.
    [Column("password_hash")]
    public string? PasswordHash { get; set; }

    [Column("role")]
    [Required]
    public string Role { get; set; } = "";

    [Column("assigned_name")]
    public string? AssignedName { get; set; }

    // True for an account whose current PasswordHash is a system-generated
    // temporary password (manual Add User, or bulk "Generate Default
    // Passwords") that the account owner has not replaced yet. A session for
    // such an account is redirected to the Change Password page for every
    // request until this clears — see SessionAuthFilterAttribute.
    [Column("must_change_password")]
    public bool MustChangePassword { get; set; }

    // Set whenever a system-generated temporary password is issued (manual
    // Add User, single "Regenerate Default Password", or the bulk "Generate
    // Default Passwords" export) — 24 hours from generation. Checked at login
    // alongside MustChangePassword so a stale temporary password can't be
    // used to sign in indefinitely; cleared to null whenever the account's
    // real password is set (self-activation, forced change, or any admin
    // reset).
    [Column("temp_password_expires_at")]
    public DateTime? TempPasswordExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
