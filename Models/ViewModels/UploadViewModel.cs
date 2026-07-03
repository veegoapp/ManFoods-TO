using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models.ViewModels;

public class UploadViewModel
{
    [Required]
    public IFormFile? File { get; set; }

    [Required]
    [Range(1, 12)]
    public int Month { get; set; }

    [Required]
    [Range(2000, 2100)]
    public int Year { get; set; }
}

public class ExitInterviewUploadViewModel
{
    [Required]
    public IFormFile? File { get; set; }
}
