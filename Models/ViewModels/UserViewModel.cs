using System.ComponentModel.DataAnnotations;
using MvcApp.Services;

namespace MvcApp.Models.ViewModels;

public class UserViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Role { get; set; } = "";
    public string AssignedName { get; set; } = "";
    public bool HasPassword { get; set; }
    /// <summary>True until the user completes the mandatory first-login password
    /// set/change (see AuthService.SetPasswordAsync). A password hash can exist
    /// (the system-generated temporary one) while this is still true, so "Active"
    /// in the UI means HasPassword && !MustChangePassword, not HasPassword alone.</summary>
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Number of stores this user's email currently matches in the latest
    /// StoreReference upload — null for roles that aren't store-restricted (Admin/User).
    /// Lets an admin catch a typo'd email before the user ever complains everything
    /// is blank.</summary>
    public int? MatchedStoreCount { get; set; }
}

public class CreateUserViewModel
{
    [Required]
    [EmailAddress]
    [MaxLength(InputLimits.Email, ErrorMessage = "Val_FieldMaxLength")]
    public string Email { get; set; } = "";

    [Required]
    [MaxLength(InputLimits.Phone, ErrorMessage = "Val_FieldMaxLength")]
    public string Phone { get; set; } = "";

    /// <summary>Shown across the portal instead of the email (Action Center notes,
    /// the Home-area header) — optional, but strongly recommended for any
    /// store-restricted role.</summary>
    [MaxLength(InputLimits.PersonName, ErrorMessage = "Val_FieldMaxLength")]
    public string? AssignedName { get; set; }

    [Required]
    public string Role { get; set; } = "";
}

public class LoginHistoryItem
{
    public DateTime LoggedInAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
    public string Portal { get; set; } = "";
    public string? FailureReason { get; set; }
    public string Browser { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
}

public class UserLoginHistoryViewModel
{
    public int UserId { get; set; }
    public string Email { get; set; } = "";
    public string AssignedName { get; set; } = "";
    public List<LoginHistoryItem> Logins { get; set; } = new();
}

public class EditUserViewModel
{
    public int Id { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(InputLimits.Email, ErrorMessage = "Val_FieldMaxLength")]
    public string Email { get; set; } = "";

    [Required]
    [MaxLength(InputLimits.Phone, ErrorMessage = "Val_FieldMaxLength")]
    public string Phone { get; set; } = "";

    [MaxLength(InputLimits.PersonName, ErrorMessage = "Val_FieldMaxLength")]
    public string? AssignedName { get; set; }

    [StrongPassword]
    public string? Password { get; set; }

    [Compare(nameof(Password))]
    public string? ConfirmPassword { get; set; }

    [Required]
    public string Role { get; set; } = "";

    // Trainer role only — ignored for every other role.
    /// <summary>Emails of the Operation Consultants this Trainer covers (the ticked boxes).</summary>
    public List<string>? TrainerConsultants { get; set; }
    /// <summary>Ticked = the Trainer may also write (Action Plan notes/tasks); unticked = read-only.</summary>
    public bool TrainerCanEdit { get; set; }
    /// <summary>Choices for the Trainer panel — filled by the controller, never posted.</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public List<MvcApp.Services.ConsultantOption> ConsultantOptions { get; set; } = new();
}
