namespace MvcApp.Services;

/// <summary>One person hired in the report's period, with what became of them.</summary>
public class NewHireStatusRow
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string PayrollGroup { get; set; } = "";
    public string CostCenter { get; set; } = "";
    public string Gender { get; set; } = "";
    public DateOnly HireDate { get; set; }
    /// <summary><see cref="NewHireStatuses"/> value.</summary>
    public string Status { get; set; } = NewHireStatuses.Active;
    public DateOnly? ResignationDate { get; set; }
    /// <summary>Days from hire to the resignation date, or to today for someone still working.</summary>
    public int TenureDays { get; set; }
    /// <summary>Resigned within 90 days of the hire date.</summary>
    public bool LeftWithin90Days { get; set; }
}

public static class NewHireStatuses
{
    public const string Active = "Active";
    public const string Resigned = "Resigned";
    /// <summary>Was on an earlier roster, is missing from the latest one, and has no resignation record.</summary>
    public const string NotOnLatestRoster = "Not on latest roster";

    public static string? Normalize(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "active" => Active,
        "resigned" => Resigned,
        _ => null,
    };
}

public interface INewHireStatusService
{
    /// <summary>People hired in the given year (and months of it, empty = all), limited to the stores the
    /// role can see and to <paramref name="stores"/> when given, newest hire first.</summary>
    Task<List<NewHireStatusRow>> GetAsync(string role, string? assignedName, int? year, IReadOnlyCollection<int>? months,
        IReadOnlyCollection<string>? stores, string? status);
}
