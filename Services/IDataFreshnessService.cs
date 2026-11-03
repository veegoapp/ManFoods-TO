namespace MvcApp.Services;

public interface IDataFreshnessService
{
    Task<DataFreshnessPeriod?> GetLatestDataPeriodAsync();

    /// <summary>When the latest period's files were uploaded (UTC); null when nothing is uploaded yet.</summary>
    Task<DateTime?> GetLatestUploadUtcAsync();
}

public sealed record DataFreshnessPeriod(int Month, int Year);