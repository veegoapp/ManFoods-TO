namespace MvcApp.Models.ViewModels;

/// <summary>
/// Stable, culture-independent identifiers for the Store Health engine's
/// pillars and engagement sub-drivers. The UI maps these to localized labels
/// client-side (via data-* attributes) exactly like the Early Warning reason
/// keys — never send a localized string as a key, so scoring/grouping stays
/// correct under both English and Arabic.
/// </summary>
public static class HealthKeys
{
    public const string PillarTurnover      = "turnover";        // overall monthly turnover
    public const string PillarEarlyAttrition = "early_attrition"; // 90-day early leavers
    public const string PillarRetention     = "retention";       // 6-month retention
    public const string PillarEngagement    = "engagement";      // exit-interview drivers
    public const string PillarLeadership    = "leadership";      // store-leader stability
    public const string PillarWorkforce     = "workforce";       // at-risk + tenure fragility + projection gap

    // Engagement sub-drivers — one per exit-interview dimension the engine reads.
    public const string DriverFairTreatment      = "fair_treatment";
    public const string DriverComplaintsHandling = "complaints_handling";
    public const string DriverBenefitsMatch      = "benefits_match";
    public const string DriverWorkload           = "workload";
    public const string DriverCommunication      = "communication";
    public const string DriverEncourageOpinions  = "encourage_opinions";
    public const string DriverTeamwork           = "teamwork";
    public const string DriverTaskFit            = "task_fit";
    public const string DriverTraining           = "training";
    public const string DriverUsePersonalAbilities = "use_personal_abilities";
}

/// <summary>One engagement sub-driver's negativity for a store, with the
/// company baseline it is measured against. Keyed stably (see HealthKeys).</summary>
public class HealthDriverDto
{
    public string Key { get; set; } = "";
    /// <summary>% of exit responses on this dimension that were negative.</summary>
    public double NegativePercent { get; set; }
    /// <summary>Company-wide negative % on the same dimension.</summary>
    public double BaselineNegativePercent { get; set; }
    public int Responses { get; set; }
    /// <summary>Graduated deviation above the company baseline (0-3).</summary>
    public int Points { get; set; }
}

/// <summary>
/// One weighted pillar of a store's health. RawValue/Baseline are the actual
/// metric and the company distribution mean it is compared against; Points is
/// the graduated 0-3 deviation (the "chips"); SubScore is the continuous
/// 0-100 severity used in the weighted composite; Evidence carries the raw
/// numbers the UI renders into a localized sentence.
/// </summary>
public class HealthPillarDto
{
    public string Key { get; set; } = "";
    public double Weight { get; set; }
    public bool HasData { get; set; }
    public double RawValue { get; set; }
    public double? Baseline { get; set; }
    public int Points { get; set; }          // 0-3 graduated deviation
    public double SubScore { get; set; }      // 0-100 severity (0 healthy, 100 severe)
    public double WeightedScore { get; set; } // SubScore * effective weight
    public string Status { get; set; } = "healthy"; // critical|high|medium|low|healthy|no_data
    public Dictionary<string, string> Evidence { get; set; } = new();
    /// <summary>Populated only for the Engagement pillar — the weak sub-drivers.</summary>
    public List<HealthDriverDto> Drivers { get; set; } = new();
}

/// <summary>One concrete, weighted action derived from a pillar finding — the
/// "real plan" content. Ordered by Weight so the most impactful work is first.</summary>
public class WeightedActionDto
{
    /// <summary>Recommendation row id when this action is backed by a persisted
    /// (checkable) recommendation; null for a purely computed action.</summary>
    public int? RecommendationId { get; set; }
    public string PillarKey { get; set; } = "";
    /// <summary>Stable action template key the UI localizes.</summary>
    public string ActionKey { get; set; } = "";
    /// <summary>Fallback text when no template key matches (legacy recommendation text).</summary>
    public string Text { get; set; } = "";
    public double Weight { get; set; }
    public string Priority { get; set; } = "medium"; // high|medium|low
    public string Evidence { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string? CompletedByName { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>One store's full health assessment for the Action Center list.</summary>
public class StoreHealthRowDto
{
    public string StoreName { get; set; } = "";
    /// <summary>0-100, higher = healthier (100 - weighted risk).</summary>
    public double HealthScore { get; set; }
    /// <summary>0-100, higher = worse (the weighted pillar composite).</summary>
    public double RiskScore { get; set; }
    public string Severity { get; set; } = "Healthy"; // Critical|High|Medium|Low|Healthy
    /// <summary>Risk weighted by how many people it affects — the ranking key.</summary>
    public double PriorityScore { get; set; }
    public int Headcount { get; set; }

    public string PlanStatus { get; set; } = "None"; // Active|Resolved|None
    public int? PlanId { get; set; }
    public int AgeDays { get; set; }
    public bool IsChronic { get; set; }
    public bool IsStalled { get; set; }
    public string Trend { get; set; } = "New"; // Improving|Worsening|Flat|New
    public string? ResponsibleName { get; set; }
    public string? ResponsibleRole { get; set; }
    public string? AssignedToName { get; set; }
    public DateOnly? TargetResolutionDate { get; set; }
    public int TasksTotal { get; set; }
    public int TasksCompleted { get; set; }

    /// <summary>Which pillar contributes the most weighted risk — the headline driver.</summary>
    public string? TopPillarKey { get; set; }
    public List<HealthPillarDto> Pillars { get; set; } = new();
}

/// <summary>Company-wide Action Center summary under the health model.</summary>
public class StoreHealthSummaryDto
{
    public int StoresEvaluated { get; set; }
    public double AvgHealthScore { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public int HealthyCount { get; set; }

    // Plan-lifecycle counts (unchanged semantics from before).
    public int ActivePlans { get; set; }
    public int ResolvedThisMonth { get; set; }
    public int StalledCount { get; set; }
    public int ChronicCount { get; set; }
    public double? AvgDaysToResolution { get; set; }

    /// <summary>Average weighted contribution of each pillar across at-risk
    /// stores — the analytical "what's driving risk company-wide" chart
    /// (replaces the old count-of-categories bar).</summary>
    public List<PillarImpactDto> PillarImpact { get; set; } = new();

    /// <summary>Company-wide negativity per engagement driver — the defensible
    /// root-cause breakdown that replaces the vague "Culture" label.</summary>
    public List<HealthDriverDto> EngagementBreakdown { get; set; } = new();

    /// <summary>Highest-priority stores (impact-ranked) for the headline list.</summary>
    public List<StoreHealthRowDto> TopPriority { get; set; } = new();

    public List<ActionCenterTrendPointDto> MonthlyTrend { get; set; } = new();

    /// <summary>Whether any workforce projection data exists at all — the UI
    /// shows the outlook pillar as "pending upload" when false.</summary>
    public bool HasProjectionData { get; set; }
}

public class PillarImpactDto
{
    public string Key { get; set; } = "";
    /// <summary>Average weighted risk contribution across evaluated stores (0-100 scale).</summary>
    public double AvgWeightedScore { get; set; }
    /// <summary>How many stores this pillar flags at Medium+ severity.</summary>
    public int StoresAffected { get; set; }
}

/// <summary>The per-store detail payload: the row's full health breakdown plus
/// the ranked weighted action plan and the store's projected workforce outlook.</summary>
public class StoreHealthDetailDto
{
    public StoreHealthRowDto Health { get; set; } = new();
    public List<WeightedActionDto> Actions { get; set; } = new();
    public WorkforceOutlookDto? Outlook { get; set; }
}

/// <summary>A store's forward staffing outlook derived from WorkforceProjection.</summary>
public class WorkforceOutlookDto
{
    public bool HasData { get; set; }
    public int CurrentHeadcount { get; set; }
    public int ProjectedHeadcount { get; set; }
    public int PlannedHires { get; set; }
    /// <summary>Projected - current. Negative = surplus, positive = shortfall.</summary>
    public int Gap { get; set; }
    /// <summary>Gap as a share of projected need (%).</summary>
    public double GapPercent { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public string Label { get; set; } = ""; // "MMM yy"
}
