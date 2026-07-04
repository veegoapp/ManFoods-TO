using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models.ViewModels;

/// <summary>
/// Active Employees + Resignations + Store Reference are uploaded together as
/// one unit: a month's data is only accepted once all three files are present.
/// </summary>
public class PeriodUploadViewModel
{
    [Required]
    [Range(1, 12)]
    public int Month { get; set; }

    [Required]
    [Range(2000, 2100)]
    public int Year { get; set; }

    [Required]
    public IFormFile? ActiveEmployeesFile { get; set; }

    [Required]
    public IFormFile? ResignationsFile { get; set; }

    [Required]
    public IFormFile? StoreReferenceFile { get; set; }
}

public class ExitInterviewUploadViewModel
{
    [Required]
    public IFormFile? File { get; set; }
}

public class BulkUserUploadViewModel
{
    [Required]
    public IFormFile? File { get; set; }
}
