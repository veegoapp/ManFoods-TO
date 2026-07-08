namespace MvcApp.Models.ViewModels;

public class NinetyDayKpiViewModel
{
    public int CohortMonth { get; set; }
    public int CohortYear { get; set; }
    public int TotalHires { get; set; }
    public int EarlyLeavers { get; set; }
    public double Rate { get; set; }
    /// <summary>True when fewer than 90 days have passed since the cohort
    /// month closed — some hires haven't had the chance to reach 90 days
    /// yet, so the rate can still rise.</summary>
    public bool IsProvisional { get; set; }
}

public class RateTrendItem
{
    public string Label { get; set; } = "";
    public double Rate { get; set; }
    public int TotalHires { get; set; }
    public int EarlyLeavers { get; set; }
    public bool IsProvisional { get; set; }
}

public class EarlyLeaverRow
{
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public DateOnly HireDate { get; set; }
    public DateOnly ResignationDate { get; set; }
    public int TenureDays { get; set; }
}

public class NinetyDayStoreRow
{
    public string StoreName { get; set; } = "";
    public string OperationConsultant { get; set; } = "";
    public string OperationManager { get; set; } = "";
    public int TotalHires { get; set; }
    public int EarlyLeavers { get; set; }
    public double Rate { get; set; }
}

public class NinetyDayOcOmRow
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int StoreCount { get; set; }
    public int TotalHires { get; set; }
    public int EarlyLeavers { get; set; }
    public double AvgRate { get; set; }
}

public class NinetyDayOcOmAnalysisResult
{
    public List<NinetyDayOcOmRow> OcRows { get; set; } = new();
    public List<NinetyDayOcOmRow> OmRows { get; set; } = new();
}
