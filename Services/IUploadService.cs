namespace MvcApp.Services;

public interface IUploadService
{
    Task<(bool success, string message, int rows)> UploadActiveEmployeesAsync(IFormFile file, int month, int year, string uploadedBy);
    Task<(bool success, string message, int rows)> UploadResignationsAsync(IFormFile file, int month, int year, string uploadedBy);
    Task<(bool success, string message, int rows)> UploadStoreReferenceAsync(IFormFile file, int month, int year, string uploadedBy);
    Task<List<MvcApp.Models.UploadLog>> GetLogsAsync();
    Task<(List<MvcApp.Models.UploadLog> Items, int TotalCount)> GetLogsPagedAsync(int page, int pageSize);
    Task DeleteLogAsync(int id);
    Task<(byte[] Content, string ContentType, string FileName)?> GetFileAsync(int id);
}
