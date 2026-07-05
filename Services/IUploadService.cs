namespace MvcApp.Services;

public interface IUploadService
{
    /// <summary>
    /// Uploads Active Employees, Resignations, and Store Reference together for
    /// one month, atomically: either all three parse and save, or none of them
    /// do. Replaces any existing data for that month/year for all three types.
    /// </summary>
    Task<(bool success, string message, Dictionary<string, int> rowCounts)> UploadPeriodDataAsync(
        IFormFile activeEmployeesFile, IFormFile resignationsFile, IFormFile storeReferenceFile,
        int month, int year, string uploadedBy);

    Task<(bool success, string message, int rows)> UploadExitInterviewsAsync(IFormFile file, string uploadedBy);

    /// <summary>
    /// Upload history grouped so the three period-tied files show as one row.
    /// Deleting any file in a period group (via <see cref="DeleteLogAsync"/>)
    /// removes the whole group and its underlying data, since the month's
    /// data is only valid with all three present.
    /// </summary>
    Task<(List<MvcApp.Models.ViewModels.UploadHistoryItem> Items, int TotalCount)> GetHistoryPagedAsync(int page, int pageSize);
    Task<List<MvcApp.Models.ViewModels.UploadHistoryItem>> GetAllHistoryAsync();
    Task DeleteLogAsync(int id);
    Task<(byte[] Content, string ContentType, string FileName)?> GetFileAsync(int id);
}
