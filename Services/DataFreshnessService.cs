using Microsoft.EntityFrameworkCore;
using MvcApp.Data;

namespace MvcApp.Services;

public sealed class DataFreshnessService : IDataFreshnessService
{
    private static readonly string[] PeriodFileTypes =
    {
        "active_employees",
        "resignations",
        "store_reference"
    };

    private readonly AppDbContext _db;

    public DataFreshnessService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DataFreshnessPeriod?> GetLatestDataPeriodAsync()
    {
        var latest = await _db.UploadLogs
            .AsNoTracking()
            .Where(log => PeriodFileTypes.Contains(log.FileType))
            .OrderByDescending(log => log.Year)
            .ThenByDescending(log => log.Month)
            .Select(log => new { log.Month, log.Year })
            .FirstOrDefaultAsync();

        return latest is null ? null : new DataFreshnessPeriod(latest.Month, latest.Year);
    }
}