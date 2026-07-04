using ClosedXML.Excel;

namespace MvcApp.Services;

public interface IReportService
{
    Task<XLWorkbook> BuildSummaryReportAsync(int month, int year, string role, string? assignedName);
    Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName);
    Task<XLWorkbook> BuildNinetyDayReportAsync();
    Task<XLWorkbook> BuildRetentionReportAsync();
    Task<XLWorkbook> BuildExitInterviewReportAsync();
    Task<XLWorkbook> BuildScorecardReportAsync();
    Task<XLWorkbook> BuildEarlyWarningReportAsync();
    Task<XLWorkbook> BuildFullReportAsync(int month, int year, string role, string? assignedName);
}
