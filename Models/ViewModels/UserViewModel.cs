using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models.ViewModels;

public class UserViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Role { get; set; } = "";
    public bool HasPassword { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUserViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Phone { get; set; } = "";

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = "";

    [Required]
    public string Role { get; set; } = "";
}

public class EditUserViewModel
{
    public int Id { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Phone { get; set; } = "";

    [MinLength(6)]
    public string? Password { get; set; }

    [Required]
    public string Role { get; set; } = "";
}
