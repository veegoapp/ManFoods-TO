using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class UploadService : IUploadService
{
    private readonly AppDbContext _db;

    public UploadService(AppDbContext db) => _db = db;

    private static string Norm(IXLCell cell) => cell.GetString().Trim();

    private static DateOnly? SafeDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime)
        {
            var dt = cell.GetDateTime();
            return DateOnly.FromDateTime(dt);
        }
        var s = cell.GetString().Trim();
        if (DateOnly.TryParse(s, out var d)) return d;
        return null;
    }

    private static string Col(IXLRow row, IXLWorksheet ws, params string[] names)
    {
        foreach (var name in names)
        {
            var col = ws.Row(1).Cells().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (col != null) return row.Cell(col.Address.ColumnNumber).GetString().Trim();
        }
        return "";
    }

    private static DateOnly? ColDate(IXLRow row, IXLWorksheet ws, params string[] names)
    {
        foreach (var name in names)
        {
            var col = ws.Row(1).Cells().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (col != null) return SafeDate(row.Cell(col.Address.ColumnNumber));
        }
        return null;
    }

    public async Task<(bool, string, int)> UploadActiveEmployeesAsync(IFormFile file, int month, int year, string uploadedBy)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        await _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year).ExecuteDeleteAsync();

        var records = new List<ActiveEmployee>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var empId = Col(row, ws, "Employee ID", "EmployeeID", "employee_id", "ID", "id");
            var name = Col(row, ws, "Name", "Employee Name", "name");
            if (string.IsNullOrEmpty(empId) && string.IsNullOrEmpty(name)) continue;

            records.Add(new ActiveEmployee
            {
                Month = month, Year = year,
                EmployeeId = empId, Name = name,
                Store = Col(row, ws, "Store", "store"),
                JobTitle = Col(row, ws, "Job Title", "JobTitle", "Position", "job_title"),
                Grade = Col(row, ws, "Grade", "grade"),
                PayrollGroup = Col(row, ws, "Payroll Group", "PayrollGroup", "payroll_group"),
                CostCenter = Col(row, ws, "Cost Center", "CostCenter", "cost_center"),
                Gender = Col(row, ws, "Gender", "gender"),
                HireDate = ColDate(row, ws, "Hire Date", "HireDate", "hire_date", "Join Date"),
            });
        }

        if (records.Count > 0) await _db.ActiveEmployees.AddRangeAsync(records);
        _db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy });
        await _db.SaveChangesAsync();

        return (true, $"Processed {records.Count} records", records.Count);
    }

    public async Task<(bool, string, int)> UploadResignationsAsync(IFormFile file, int month, int year, string uploadedBy)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        await _db.Resignations.Where(r => r.Month == month && r.Year == year).ExecuteDeleteAsync();

        var records = new List<Resignation>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var empId = Col(row, ws, "Employee ID", "EmployeeID", "employee_id", "ID");
            var name = Col(row, ws, "Name", "Employee Name", "name");
            if (string.IsNullOrEmpty(empId) && string.IsNullOrEmpty(name)) continue;

            records.Add(new Resignation
            {
                Month = month, Year = year,
                EmployeeId = empId, Name = name,
                Store = Col(row, ws, "Store", "store"),
                JobTitle = Col(row, ws, "Job Title", "JobTitle", "Position", "job_title"),
                Gender = Col(row, ws, "Gender", "gender"),
                HireDate = ColDate(row, ws, "Hire Date", "HireDate", "hire_date", "Join Date"),
                ResignationDate = ColDate(row, ws, "Resignation Date", "ResignationDate", "resignation_date", "Last Day"),
                PayrollGroup = Col(row, ws, "Payroll Group", "PayrollGroup", "payroll_group"),
                CostCenter = Col(row, ws, "Cost Center", "CostCenter", "cost_center"),
            });
        }

        if (records.Count > 0) await _db.Resignations.AddRangeAsync(records);
        _db.UploadLogs.Add(new UploadLog { FileType = "resignations", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy });
        await _db.SaveChangesAsync();

        return (true, $"Processed {records.Count} records", records.Count);
    }

    public async Task<(bool, string, int)> UploadStoreReferenceAsync(IFormFile file, int month, int year, string uploadedBy)
    {
        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        await _db.StoreReferences.Where(s => s.Month == month && s.Year == year).ExecuteDeleteAsync();

        var records = new List<StoreReference>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var storeName = Col(row, ws, "Store Name", "StoreName", "Store", "store_name");
            if (string.IsNullOrEmpty(storeName)) continue;

            records.Add(new StoreReference
            {
                Month = month, Year = year,
                StoreName = storeName,
                StoreLeader = Col(row, ws, "Store Leader", "StoreLeader", "store_leader"),
                OperationConsultant = Col(row, ws, "Operation Consultant", "OperationConsultant", "OC", "Consultant"),
                OperationManager = Col(row, ws, "Operation Manager", "OperationManager", "OM", "Manager"),
            });
        }

        if (records.Count > 0) await _db.StoreReferences.AddRangeAsync(records);
        _db.UploadLogs.Add(new UploadLog { FileType = "store_reference", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy });
        await _db.SaveChangesAsync();

        return (true, $"Processed {records.Count} records", records.Count);
    }

    public async Task<List<UploadLog>> GetLogsAsync() =>
        await _db.UploadLogs.OrderByDescending(l => l.UploadDate).ToListAsync();

    public async Task DeleteLogAsync(int id)
    {
        var log = await _db.UploadLogs.FindAsync(id);
        if (log == null) return;

        if (log.FileType == "active_employees")
            await _db.ActiveEmployees.Where(e => e.Month == log.Month && e.Year == log.Year).ExecuteDeleteAsync();
        else if (log.FileType == "resignations")
            await _db.Resignations.Where(r => r.Month == log.Month && r.Year == log.Year).ExecuteDeleteAsync();
        else if (log.FileType == "store_reference")
            await _db.StoreReferences.Where(s => s.Month == log.Month && s.Year == log.Year).ExecuteDeleteAsync();

        _db.UploadLogs.Remove(log);
        await _db.SaveChangesAsync();
    }
}
