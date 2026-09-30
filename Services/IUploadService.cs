namespace MvcApp.Services;

public interface IUploadService
{
    /// <summary>
    /// Uploads Active Employees, Resignations, and Store Reference together for
    /// one month, atomically: either all three parse and save, or none of them
    /// do. Replaces any existing data for that month/year for all three types.
    /// </summary>
    Task<(bool success, string message, Dictionary<string, int> rowCounts, string? warning)> UploadPeriodDataAsync(
        IFormFile activeEmployeesFile, IFormFile resignationsFile, IFormFile storeReferenceFile,
        int month, int year, string uploadedBy);

    /// <summary>Month/year pairs that already have period data uploaded — lets the
    /// upload form warn before an upload silently replaces an existing period.</summary>
    Task<List<(int Month, int Year)>> GetExistingPeriodKeysAsync();

    Task<(bool success, string message, int rows)> UploadExitInterviewsAsync(IFormFile file, string uploadedBy);

    /// <summary>
    /// Upload history grouped so the three period-tied files show as one row.
    /// Deleting any file in a period group (via <see cref="DeleteLogAsync"/>)
    /// removes the whole group and its underlying data, since the month's
    /// data is only valid with all three present.
    /// </summary>
    Task<(List<MvcApp.Models.ViewModels.UploadHistoryItem> Items, int TotalCount)> GetHistoryPagedAsync(int page, int pageSize, string sort = "date", string dir = "desc");
    Task<List<MvcApp.Models.ViewModels.UploadHistoryItem>> GetAllHistoryAsync();
    Task DeleteLogAsync(int id);
    Task<(byte[] Content, string ContentType, string FileName)?> GetFileAsync(int id);

    /// <summary>Reads a single uploaded file's raw bytes and returns its first
    /// sheet as a header row + capped data rows, for an in-portal preview.</summary>
    Task<MvcApp.Models.ViewModels.UploadFilePreview?> PreviewFileAsync(int logId, int maxRows = 300);

    /// <summary>
    /// Replaces a single file type (active_employees, resignations, or
    /// store_reference) for a period that already exists, leaving the
    /// other two files untouched.
    /// </summary>
    Task<(bool success, string message, string? warning)> UpdateSingleFileAsync(
        string fileType, int month, int year, IFormFile file, string uploadedBy);
}
