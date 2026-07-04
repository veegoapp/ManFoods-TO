namespace MvcApp.Models.ViewModels;

public class UploadFileRef
{
    public int LogId { get; set; }
    public string FileType { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool HasFile { get; set; }
}

/// <summary>
/// One row in the Upload History table. "Period" rows group the three
/// month-tied files (Active Employees / Resignations / Store Reference)
/// that were uploaded and validated together; "ExitInterviews" rows are a
/// single standalone Forms-export upload, not tied to a specific month.
/// </summary>
public class UploadHistoryItem
{
    public string Kind { get; set; } = ""; // "period" | "exit_interviews"
    public int? Month { get; set; }
    public int? Year { get; set; }
    public DateTime UploadDate { get; set; }
    public string UploadedBy { get; set; } = "";
    public int PrimaryLogId { get; set; }
    public List<UploadFileRef> Files { get; set; } = new();
}
