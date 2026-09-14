using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Computes the Store Health Index — a weighted composite of six pillars, each
/// scored by how far a store deviates from the company-wide distribution for
/// that metric (a relative, self-calibrating measure, not an arbitrary fixed
/// threshold). Every pillar reuses an existing analytics service so its figures
/// match the rest of the portal, and each pillar contributes a 0-100 severity
/// sub-score weighted into the store's overall risk. The engagement pillar
/// decomposes into the real exit-interview dimensions (fairness, workload,
/// communication…) so a diagnosis names the specific driver the leaving
/// employees actually flagged, rather than a vague "culture" label.
/// </summary>
public class StoreHealthService : IStoreHealthService
{
    private readonly AppDbContext _db;
    private readonly IStoreService _stores;
    private readonly IActionPlanRoleService _roles;
    private readonly IDashboardService _dashboard;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IRetentionService _retention;
    private readonly IEarlyWarningService _earlyWarning;
    private readonly IExitInterviewService _exitInterview;
    private readonly IWorkforceProjectionService _projection;

    private const string SystemRole = "Admin"; // company-wide baselines ignore the viewer's store scope

    // ── Pillar weights (sum = 1.0). Effective weights are renormalised per store
    //    over only the pillars that have data, so a store missing (say) exit
    //    interviews or a projection is scored fairly on what it does have. ──
    private const double WTurnover   = 0.24;
    private const double WEarly      = 0.20;
    private const double WRetention  = 0.16;
    private const double WEngagement = 0.18;
    private const double WLeadership = 0.10;
    private const double WWorkforce  = 0.12;

    // Severity tiers from the 0-100 weighted risk score.
    private const double CriticalAt = 60, HighAt = 42, MediumAt = 25, LowAt = 12;

    // Standard-deviation floors keep a tight, low-variance distribution from
    // turning a small absolute difference into a huge z-score (same guard the
    // Early Warning engine uses). Units are the metric's own (percentage points).
    private const double StdFloorTurnover = 4.0, StdFloorEarly = 5.0, StdFloorRetention = 6.0,
                         StdFloorEngagement = 6.0, StdFloorAtRisk = 3.0;

    private const int MinHeadcountForRate = 5;
    private const int MinEngagementResponses = 2;
    private const int LeadershipLookbackMonths = 6;
    private const int StalledAgeDaysThreshold = 45;
    private const double TrendFlatMarginPoints = 1.0;

    public StoreHealthService(
        AppDbContext db,
        IStoreService stores,
        IActionPlanRoleService roles,
        IDashboardService dashboard,
        INinetyDayTurnoverService ninetyDay,
        IRetentionService retention,
        IEarlyWarningService earlyWarning,
        IExitInterviewService exitInterview,
        IWorkforceProjectionService projection)
    {
        _db = db;
        _stores = stores;
        _roles = roles;
        _dashboard = dashboard;
        _ninetyDay = ninetyDay;
        _retention = retention;
        _earlyWarning = earlyWarning;
        _exitInterview = exitInterview;
        _projection = projection;
    }

    // ───────────────────────────── Public API ─────────────────────────────

    public async Task<List<StoreHealthRowDto>> GetStoreHealthRowsAsync(string role, string? email)
    {
        var accessible = await AccessibleStoreNamesAsync(role, email);
        if (accessible.Count == 0) return new List<StoreHealthRowDto>();
        var comp = await ComputeAsync(accessible);
        return comp.Rows.OrderByDescending(r => r.PriorityScore).ThenBy(r => r.StoreName).ToList();
    }

    public async Task<StoreHealthSummaryDto> GetSummaryAsync(string role, string? email)
    {
        var summary = new StoreHealthSummaryDto();
        var accessible = await AccessibleStoreNamesAsync(role, email);
        summary.HasProjectionData = await _projection.HasAnyAsync();
        if (accessible.Count == 0) return summary;

        var comp = await ComputeAsync(accessible);
        var rows = comp.Rows;

        summary.StoresEvaluated = rows.Count;
        summary.AvgHealthScore = rows.Count > 0 ? Math.Round(rows.Average(r => r.HealthScore), 1) : 0;
        summary.CriticalCount = rows.Count(r => r.Severity == "Critical");
        summary.HighCount = rows.Count(r => r.Severity == "High");
        summary.MediumCount = rows.Count(r => r.Severity == "Medium");
        summary.LowCount = rows.Count(r => r.Severity == "Low");
        summary.HealthyCount = rows.Count(r => r.Severity == "Healthy");

        // Plan-lifecycle counts (same semantics the old summary reported).
        var plans = await _db.StoreActionPlans.Where(p => accessible.Contains(p.StoreName)).ToListAsync();
        var now = DateTime.UtcNow;
        summary.ActivePlans = plans.Count(p => p.Status == "Active");
        var resolved = plans.Where(p => p.Status == "Resolved" && p.ResolvedAt.HasValue).ToList();
        summary.ResolvedThisMonth = resolved.Count(p => p.ResolvedAt!.Value.Year == now.Year && p.ResolvedAt.Value.Month == now.Month);
        summary.AvgDaysToResolution = resolved.Count > 0
            ? Math.Round(resolved.Average(p => (p.ResolvedAt!.Value - p.CreatedAt).TotalDays), 1)
            : null;
        summary.StalledCount = rows.Count(r => r.IsStalled);
        summary.ChronicCount = rows.Count(r => r.IsChronic);

        // Pillar impact: average weighted contribution + how many stores each
        // pillar flags at Medium+ — the analytical "what's driving risk" chart.
        foreach (var key in AllPillarKeys)
        {
            var contributing = rows.Select(r => r.Pillars.FirstOrDefault(p => p.Key == key && p.HasData))
                                    .Where(p => p != null).Select(p => p!).ToList();
            if (contributing.Count == 0) continue;
            summary.PillarImpact.Add(new PillarImpactDto
            {
                Key = key,
                AvgWeightedScore = Math.Round(contributing.Average(p => p.WeightedScore), 1),
                StoresAffected = contributing.Count(p => p.Status is "Critical" or "High" or "Medium"),
            });
        }
        summary.PillarImpact = summary.PillarImpact.OrderByDescending(p => p.AvgWeightedScore).ToList();

        // Company-wide engagement-driver negativity (the defensible root-cause breakdown).
        summary.EngagementBreakdown = comp.CompanyDrivers
            .OrderByDescending(d => d.NegativePercent)
            .ToList();

        summary.TopPriority = rows.OrderByDescending(r => r.PriorityScore)
            .Where(r => r.Severity != "Healthy")
            .Take(8).ToList();

        summary.MonthlyTrend = await BuildMonthlyTrendAsync(accessible);
        return summary;
    }

    public async Task<StoreHealthDetailDto?> GetDetailAsync(string storeName, string role, string? email)
    {
        var accessible = await AccessibleStoreNamesAsync(role, email);
        var match = accessible.FirstOrDefault(s => string.Equals(s, storeName, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        var comp = await ComputeAsync(accessible);
        var row = comp.Rows.FirstOrDefault(r => string.Equals(r.StoreName, match, StringComparison.OrdinalIgnoreCase));
        if (row == null) return null;

        var detail = new StoreHealthDetailDto { Health = row };
        detail.Actions = await BuildWeightedActionsAsync(row);
        detail.Outlook = await BuildOutlookAsync(match, row.Headcount);
        return detail;
    }

    // ─────────────────────────── Core computation ───────────────────────────

    private static readonly string[] AllPillarKeys =
    {
        HealthKeys.PillarTurnover, HealthKeys.PillarEarlyAttrition, HealthKeys.PillarRetention,
        HealthKeys.PillarEngagement, HealthKeys.PillarLeadership, HealthKeys.PillarWorkforce,
    };

    private sealed class HealthComputation
    {
        public List<StoreHealthRowDto> Rows = new();
        public List<HealthDriverDto> CompanyDrivers = new();
    }

    private async Task<HealthComputation> ComputeAsync(List<string> stores)
    {
        var result = new HealthComputation();

        // Anchor period — the latest one with uploaded data.
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        var latest = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).FirstOrDefault();
        if (latest == null) return result; // no data at all → nothing to score

        int month = latest.Month, year = latest.Year;

        // ── Raw per-store metrics (company-wide, unscoped, for honest baselines) ──
        var turnoverRows = await _dashboard.GetStoreComparisonAsync(month, year, SystemRole, null);
        var turnover = turnoverRows.ToDictionary(r => r.StoreName, r => r, StringComparer.OrdinalIgnoreCase);

        var ninetyRows = await _ninetyDay.GetStoreComparisonAsync(month, year, SystemRole, null);
        var ninety = ninetyRows.ToDictionary(r => r.StoreName, r => r, StringComparer.OrdinalIgnoreCase);

        var retentionRows = await _retention.GetStoreRetentionRankingAsync(null, SystemRole, null, null, null, null, null, month, year);
        var retention = retentionRows.ToDictionary(r => r.Label, r => (double)r.Value, StringComparer.OrdinalIgnoreCase);

        var engagementByStore = await _exitInterview.GetStoreEngagementProfilesAsync(SystemRole, null);

        var leaderCounts = await LeaderChangeCountsAsync(month, year);

        var watchlist = await _earlyWarning.GetWatchlistAsync(null, SystemRole, null);
        var highRiskByStore = watchlist.Where(w => w.Stars >= 4)
            .GroupBy(w => w.Store, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var projections = await _projection.GetLatestByStoreAsync(stores);

        // ── Company distributions (across every store with enough data) ──
        var turnoverDist = Dist(turnover.Values.Where(r => r.Headcount >= MinHeadcountForRate).Select(r => r.TurnoverRate), StdFloorTurnover);
        var earlyDist    = Dist(ninety.Values.Where(r => r.TotalHires >= MinHeadcountForRate).Select(r => r.Rate), StdFloorEarly);
        var retentionDist = Dist(retention.Values, StdFloorRetention);

        // Per-driver engagement baselines (company mean negativity for each dimension).
        var driverBaselines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var driverGroup in engagementByStore.Values.SelectMany(v => v).GroupBy(d => d.Key))
            driverBaselines[driverGroup.Key] = driverGroup.Average(d => d.NegativePercent);
        result.CompanyDrivers = driverBaselines
            .Select(kv => new HealthDriverDto { Key = kv.Key, NegativePercent = Math.Round(kv.Value, 1), BaselineNegativePercent = Math.Round(kv.Value, 1) })
            .ToList();

        // At-risk ratio distribution (high-risk employees per 100 headcount).
        var atRiskRatios = new List<double>();
        foreach (var s in turnover.Values.Where(r => r.Headcount >= MinHeadcountForRate))
        {
            var hr = highRiskByStore.TryGetValue(s.StoreName, out var c) ? c : 0;
            atRiskRatios.Add(s.Headcount > 0 ? hr * 100.0 / s.Headcount : 0);
        }
        var atRiskDist = Dist(atRiskRatios, StdFloorAtRisk);

        // ── Plan-lifecycle data (status, responsible, tasks, trend, chronic/stalled) ──
        var responsibleByStore = await _roles.GetEffectiveResponsiblePartiesAsync(stores);
        var allPlans = await _db.StoreActionPlans.Where(p => stores.Contains(p.StoreName)).ToListAsync();
        var plansByStore = allPlans.GroupBy(p => p.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var latestPlanIds = plansByStore.Values.Select(l => l.OrderByDescending(p => p.CreatedAt).First().Id).ToList();
        var recsByPlan = (await _db.ActionPlanRecommendations.Where(r => latestPlanIds.Contains(r.StoreActionPlanId)).ToListAsync())
            .GroupBy(r => r.StoreActionPlanId).ToDictionary(g => g.Key, g => g.ToList());
        var snapshotsByPlan = (await _db.ActionPlanMetricSnapshots.Where(s => latestPlanIds.Contains(s.StoreActionPlanId)).ToListAsync())
            .GroupBy(s => s.StoreActionPlanId).ToDictionary(g => g.Key, g => g.ToList());

        var asOf = DateTime.UtcNow;

        foreach (var store in stores)
        {
            var pillars = new List<HealthPillarDto>();
            turnover.TryGetValue(store, out var tRow);
            int headcount = tRow?.Headcount ?? 0;

            // 1 ── Turnover
            pillars.Add(tRow != null && tRow.Headcount >= MinHeadcountForRate
                ? RatePillar(HealthKeys.PillarTurnover, WTurnover, tRow.TurnoverRate, turnoverDist, higherIsWorse: true,
                    new() { ["value"] = tRow.TurnoverRate.ToString("F1"), ["baseline"] = turnoverDist.Mean.ToString("F1") })
                : NoData(HealthKeys.PillarTurnover, WTurnover));

            // 2 ── Early attrition (90-day)
            ninety.TryGetValue(store, out var nRow);
            pillars.Add(nRow != null && nRow.TotalHires >= MinHeadcountForRate
                ? RatePillar(HealthKeys.PillarEarlyAttrition, WEarly, nRow.Rate, earlyDist, higherIsWorse: true,
                    new() { ["value"] = nRow.Rate.ToString("F1"), ["baseline"] = earlyDist.Mean.ToString("F1") })
                : NoData(HealthKeys.PillarEarlyAttrition, WEarly));

            // 3 ── Retention (6-month share of active workforce; lower is worse)
            pillars.Add(retention.TryGetValue(store, out var rVal)
                ? RatePillar(HealthKeys.PillarRetention, WRetention, rVal, retentionDist, higherIsWorse: false,
                    new() { ["value"] = rVal.ToString("F0"), ["baseline"] = retentionDist.Mean.ToString("F0") })
                : NoData(HealthKeys.PillarRetention, WRetention));

            // 4 ── Engagement (exit-interview drivers)
            pillars.Add(engagementByStore.TryGetValue(store, out var drivers) && drivers.Sum(d => d.Responses) >= MinEngagementResponses
                ? EngagementPillar(drivers, driverBaselines)
                : NoData(HealthKeys.PillarEngagement, WEngagement));

            // 5 ── Leadership stability
            pillars.Add(LeadershipPillar(leaderCounts.TryGetValue(store, out var lc) ? lc : 1));

            // 6 ── Workforce outlook (at-risk employees + projected staffing gap)
            pillars.Add(WorkforcePillar(store, headcount, highRiskByStore, atRiskDist, projections));

            // ── Weighted composite over pillars that have data ──
            var withData = pillars.Where(p => p.HasData).ToList();
            double totalW = withData.Sum(p => p.Weight);
            double risk = 0;
            if (totalW > 0)
            {
                foreach (var p in withData)
                {
                    p.WeightedScore = Math.Round(p.SubScore * (p.Weight / totalW), 1);
                    risk += p.SubScore * (p.Weight / totalW);
                }
            }
            risk = Math.Round(Math.Clamp(risk, 0, 100), 1);

            var row = new StoreHealthRowDto
            {
                StoreName = store,
                RiskScore = risk,
                HealthScore = Math.Round(100 - risk, 1),
                Severity = SeverityOf(risk),
                Headcount = headcount,
                PriorityScore = Math.Round(risk * Math.Sqrt(Math.Max(headcount, 1)), 1),
                Pillars = pillars,
                TopPillarKey = withData.OrderByDescending(p => p.WeightedScore).FirstOrDefault()?.Key,
                ResponsibleName = responsibleByStore.TryGetValue(store, out var rp) ? rp?.Name : null,
                ResponsibleRole = responsibleByStore.TryGetValue(store, out var rp2) ? rp2?.Role : null,
            };

            // Merge plan-lifecycle state.
            if (plansByStore.TryGetValue(store, out var storePlans))
            {
                var plan = storePlans.OrderByDescending(p => p.CreatedAt).First();
                var recs = recsByPlan.TryGetValue(plan.Id, out var rr) ? rr : new List<ActionPlanRecommendation>();
                var snaps = snapshotsByPlan.TryGetValue(plan.Id, out var ss) ? ss : new List<ActionPlanMetricSnapshot>();
                var ageDays = (int)((plan.Status == "Resolved" && plan.ResolvedAt.HasValue ? plan.ResolvedAt.Value : asOf) - plan.CreatedAt).TotalDays;
                row.PlanStatus = plan.Status;
                row.PlanId = plan.Id;
                row.AgeDays = ageDays;
                row.IsChronic = storePlans.Count > 1;
                row.TasksTotal = recs.Count;
                row.TasksCompleted = recs.Count(x => x.IsCompleted);
                row.IsStalled = plan.Status == "Active" && ageDays >= StalledAgeDaysThreshold && row.TasksCompleted == 0;
                row.Trend = ComputeTrend(snaps);
                row.AssignedToName = plan.AssignedToName;
                row.TargetResolutionDate = plan.TargetResolutionDate;
            }

            result.Rows.Add(row);
        }

        return result;
    }

    // ─────────────────────────── Pillar builders ───────────────────────────

    private static HealthPillarDto RatePillar(string key, double weight, double value, (double Mean, double Std) dist,
        bool higherIsWorse, Dictionary<string, string> evidence)
    {
        double z = higherIsWorse ? (value - dist.Mean) / dist.Std : (dist.Mean - value) / dist.Std;
        int points = PointsOf(z);
        double sub = SubScoreOf(z);
        return new HealthPillarDto
        {
            Key = key, Weight = weight, HasData = true,
            RawValue = Math.Round(value, 1), Baseline = Math.Round(dist.Mean, 1),
            Points = points, SubScore = sub, Status = StatusOf(sub), Evidence = evidence,
        };
    }

    private static HealthPillarDto EngagementPillar(List<HealthDriverDto> drivers, Dictionary<string, double> baselines)
    {
        // Score each dimension against its own company baseline; the pillar takes
        // the worst two drivers so one bad question doesn't dominate but a broadly
        // negative store still scores high. Weak drivers (points > 0) are surfaced.
        var scored = new List<(HealthDriverDto Driver, double Sub)>();
        foreach (var d in drivers)
        {
            double baseline = baselines.TryGetValue(d.Key, out var b) ? b : d.NegativePercent;
            double std = Math.Max(StdFloorEngagement, 0);
            double z = (d.NegativePercent - baseline) / std;
            d.BaselineNegativePercent = Math.Round(baseline, 1);
            d.Points = PointsOf(z);
            scored.Add((d, SubScoreOf(z)));
        }

        var topTwo = scored.OrderByDescending(x => x.Sub).Take(2).ToList();
        double sub = topTwo.Count > 0 ? topTwo.Average(x => x.Sub) : 0;
        var weak = scored.Where(x => x.Driver.Points > 0).OrderByDescending(x => x.Driver.Points)
            .Select(x => x.Driver).ToList();
        var worst = scored.OrderByDescending(x => x.Sub).FirstOrDefault().Driver;

        return new HealthPillarDto
        {
            Key = HealthKeys.PillarEngagement, Weight = WEngagement, HasData = true,
            RawValue = worst != null ? worst.NegativePercent : 0,
            Baseline = worst != null ? worst.BaselineNegativePercent : (double?)null,
            Points = weak.Count > 0 ? weak.Max(d => d.Points) : 0,
            SubScore = sub, Status = StatusOf(sub),
            Drivers = weak,
            Evidence = worst != null
                ? new() { ["driver"] = worst.Key, ["value"] = worst.NegativePercent.ToString("F0"), ["baseline"] = worst.BaselineNegativePercent.ToString("F0") }
                : new(),
        };
    }

    private static HealthPillarDto LeadershipPillar(int distinctLeaders)
    {
        // Discrete: leader counts are small integers where a z-score is noisy.
        // 1 leader = stable; each additional leader in the window escalates.
        int points = distinctLeaders >= 4 ? 3 : distinctLeaders == 3 ? 2 : distinctLeaders == 2 ? 1 : 0;
        double sub = points switch { 3 => 100, 2 => 70, 1 => 40, _ => 0 };
        return new HealthPillarDto
        {
            Key = HealthKeys.PillarLeadership, Weight = WLeadership, HasData = true,
            RawValue = distinctLeaders, Points = points, SubScore = sub, Status = StatusOf(sub),
            Evidence = new() { ["leaders"] = distinctLeaders.ToString(), ["months"] = LeadershipLookbackMonths.ToString() },
        };
    }

    private static HealthPillarDto WorkforcePillar(string store, int headcount,
        Dictionary<string, int> highRiskByStore, (double Mean, double Std) atRiskDist,
        Dictionary<string, WorkforceProjectionSnapshot> projections)
    {
        double atRiskSub = 0; int atRiskPoints = 0;
        int highRisk = highRiskByStore.TryGetValue(store, out var c) ? c : 0;
        var evidence = new Dictionary<string, string> { ["atRisk"] = highRisk.ToString() };
        bool hasAtRisk = headcount >= MinHeadcountForRate;
        if (hasAtRisk)
        {
            double ratio = headcount > 0 ? highRisk * 100.0 / headcount : 0;
            double z = (ratio - atRiskDist.Mean) / atRiskDist.Std;
            atRiskSub = SubScoreOf(z);
            atRiskPoints = PointsOf(z);
        }

        // Projected staffing gap (forward-looking). Only a shortfall is risk.
        double gapSub = 0; int gapPoints = 0; bool hasGap = false;
        if (projections.TryGetValue(store, out var proj) && proj.ProjectedHeadcount > 0)
        {
            hasGap = true;
            int gap = proj.ProjectedHeadcount - headcount;
            double gapPct = gap > 0 ? gap * 100.0 / proj.ProjectedHeadcount : 0;
            gapSub = Math.Clamp(gapPct, 0, 25) / 25.0 * 100.0;
            gapPoints = gapPct >= 20 ? 3 : gapPct >= 12 ? 2 : gapPct >= 5 ? 1 : 0;
            evidence["gap"] = gap.ToString();
            evidence["projected"] = proj.ProjectedHeadcount.ToString();
            evidence["gapPct"] = gapPct.ToString("F0");
        }

        bool hasData = hasAtRisk || hasGap;
        // Blend the two components when both exist (gap weighted a touch higher as
        // the only forward signal); otherwise take whichever is present.
        double sub = (hasAtRisk, hasGap) switch
        {
            (true, true) => atRiskSub * 0.45 + gapSub * 0.55,
            (true, false) => atRiskSub,
            (false, true) => gapSub,
            _ => 0,
        };

        return new HealthPillarDto
        {
            Key = HealthKeys.PillarWorkforce, Weight = WWorkforce, HasData = hasData,
            RawValue = highRisk, Points = Math.Max(atRiskPoints, gapPoints),
            SubScore = sub, Status = hasData ? StatusOf(sub) : "no_data", Evidence = evidence,
        };
    }

    private static HealthPillarDto NoData(string key, double weight) =>
        new() { Key = key, Weight = weight, HasData = false, Status = "no_data" };

    // ─────────────────────────── Weighted actions ───────────────────────────

    /// <summary>Maps each at-risk pillar to concrete, weighted action templates,
    /// linked to the store's persisted recommendation rows (so they stay
    /// checkable) where the pillar's category matches. Ordered by weight.</summary>
    private async Task<List<WeightedActionDto>> BuildWeightedActionsAsync(StoreHealthRowDto row)
    {
        var actions = new List<WeightedActionDto>();
        if (row.PlanId == null && row.Severity == "Healthy") return actions;

        // Load persisted recommendations for the store's latest plan, so computed
        // actions can carry a real (toggle-able) recommendation id.
        List<ActionPlanRecommendation> recs = new();
        if (row.PlanId is { } planId)
            recs = await _db.ActionPlanRecommendations.Where(r => r.StoreActionPlanId == planId).ToListAsync();
        var recsByPillar = recs.GroupBy(r => CategoryToPillar(r.Category))
            .ToDictionary(g => g.Key, g => new Queue<ActionPlanRecommendation>(g.OrderBy(r => r.IsCompleted).ThenBy(r => r.Id)));

        foreach (var pillar in row.Pillars.Where(p => p.HasData && p.SubScore > 0).OrderByDescending(p => p.WeightedScore))
        {
            foreach (var actionKey in ActionTemplates(pillar))
            {
                var dto = new WeightedActionDto
                {
                    PillarKey = pillar.Key,
                    ActionKey = actionKey,
                    Weight = Math.Round(pillar.WeightedScore, 1),
                    Priority = pillar.Status is "Critical" or "High" ? "high" : pillar.Status == "Medium" ? "medium" : "low",
                    Evidence = BuildEvidence(pillar),
                };
                if (recsByPillar.TryGetValue(pillar.Key, out var q) && q.Count > 0)
                {
                    var rec = q.Dequeue();
                    dto.RecommendationId = rec.Id;
                    dto.IsCompleted = rec.IsCompleted;
                    dto.CompletedByName = rec.CompletedByName;
                    dto.CompletedAt = rec.CompletedAt;
                    if (string.IsNullOrWhiteSpace(actionKey)) dto.Text = rec.RecommendationText;
                }
                actions.Add(dto);
            }
        }
        return actions.OrderByDescending(a => a.Weight).ToList();
    }

    private async Task<WorkforceOutlookDto> BuildOutlookAsync(string store, int currentHeadcount)
    {
        var map = await _projection.GetLatestByStoreAsync(new[] { store });
        if (!map.TryGetValue(store, out var proj) || proj.ProjectedHeadcount <= 0)
            return new WorkforceOutlookDto { HasData = false, CurrentHeadcount = currentHeadcount };

        int gap = proj.ProjectedHeadcount - currentHeadcount;
        return new WorkforceOutlookDto
        {
            HasData = true,
            CurrentHeadcount = currentHeadcount,
            ProjectedHeadcount = proj.ProjectedHeadcount,
            PlannedHires = proj.PlannedHires,
            Gap = gap,
            GapPercent = proj.ProjectedHeadcount > 0 ? Math.Round(gap * 100.0 / proj.ProjectedHeadcount, 1) : 0,
            Month = proj.Month,
            Year = proj.Year,
            Label = MonthLabel(proj.Month, proj.Year),
        };
    }

    // ─────────────────────────────── Helpers ───────────────────────────────

    private async Task<List<string>> AccessibleStoreNamesAsync(string role, string? email)
    {
        var refs = await _stores.GetStoresAsync(null, null, role, email);
        return refs.Select(s => s.StoreName?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<Dictionary<string, int>> LeaderChangeCountsAsync(int month, int year)
    {
        int currentKey = year * 12 + month;
        int cutoffKey = currentKey - (LeadershipLookbackMonths - 1);
        var refs = await _db.StoreReferences
            .Where(s => (s.Year * 12 + s.Month) >= cutoffKey && (s.Year * 12 + s.Month) <= currentKey)
            .Select(s => new { s.StoreName, s.StoreLeader })
            .ToListAsync();
        return refs.Where(r => !string.IsNullOrWhiteSpace(r.StoreName))
            .GroupBy(r => r.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => Math.Max(1, g.Select(x => x.StoreLeader).Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<ActionCenterTrendPointDto>> BuildMonthlyTrendAsync(List<string> stores)
    {
        var plans = await _db.StoreActionPlans.Where(p => stores.Contains(p.StoreName)).ToListAsync();
        var points = new List<ActionCenterTrendPointDto>();
        var now = DateTime.UtcNow;
        for (int i = 5; i >= 0; i--)
        {
            var d = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            points.Add(new ActionCenterTrendPointDto
            {
                Label = MonthLabel(d.Month, d.Year),
                Opened = plans.Count(p => p.CreatedAt.Year == d.Year && p.CreatedAt.Month == d.Month),
                Resolved = plans.Count(p => p.ResolvedAt.HasValue && p.ResolvedAt.Value.Year == d.Year && p.ResolvedAt.Value.Month == d.Month),
            });
        }
        return points;
    }

    private static (double Mean, double Std) Dist(IEnumerable<double> values, double floor)
    {
        var list = values.ToList();
        if (list.Count == 0) return (0, floor);
        double mean = list.Average();
        double std = list.Count < 2 ? 0 : Math.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / list.Count);
        return (mean, Math.Max(std, floor));
    }

    private static int PointsOf(double z) => z >= 2.0 ? 3 : z >= 1.5 ? 2 : z >= 1.0 ? 1 : 0;

    /// <summary>Continuous 0-100 severity from a deviation z-score: nothing below
    /// the mean, saturating at +2.5σ. Keeps the composite smooth rather than
    /// bucketed, while PointsOf gives the discrete "chips" for the UI.</summary>
    private static double SubScoreOf(double z) => Math.Round(Math.Clamp(z, 0, 2.5) / 2.5 * 100, 1);

    private static string StatusOf(double sub) =>
        sub >= 80 ? "Critical" : sub >= 55 ? "High" : sub >= 30 ? "Medium" : sub > 0 ? "Low" : "healthy";

    private static string SeverityOf(double risk) =>
        risk >= CriticalAt ? "Critical" : risk >= HighAt ? "High" : risk >= MediumAt ? "Medium" : risk >= LowAt ? "Low" : "Healthy";

    private static string ComputeTrend(List<ActionPlanMetricSnapshot> snapshots)
    {
        var withTurnover = snapshots.Where(s => s.TurnoverRate.HasValue).OrderBy(s => s.Year).ThenBy(s => s.Month).ToList();
        if (withTurnover.Count < 2) return "New";
        var delta = withTurnover.Last().TurnoverRate!.Value - withTurnover.First().TurnoverRate!.Value;
        if (delta <= -TrendFlatMarginPoints) return "Improving";
        if (delta >= TrendFlatMarginPoints) return "Worsening";
        return "Flat";
    }

    private static string CategoryToPillar(string category) => category switch
    {
        "Retention" => HealthKeys.PillarTurnover,
        "Onboarding" => HealthKeys.PillarEarlyAttrition,
        "Leadership" => HealthKeys.PillarLeadership,
        "Culture" => HealthKeys.PillarEngagement,
        "Monitoring" => HealthKeys.PillarWorkforce,
        _ => HealthKeys.PillarTurnover,
    };

    /// <summary>Concrete action template keys per pillar — the "what to do".
    /// For engagement, the dominant weak driver selects a targeted action.</summary>
    private static IEnumerable<string> ActionTemplates(HealthPillarDto pillar)
    {
        switch (pillar.Key)
        {
            case HealthKeys.PillarTurnover:
                yield return "Action_Turnover_StayInterviews";
                yield return "Action_Turnover_Workload";
                break;
            case HealthKeys.PillarEarlyAttrition:
                yield return "Action_Early_Onboarding";
                yield return "Action_Early_Buddy";
                break;
            case HealthKeys.PillarRetention:
                yield return "Action_Retention_Growth";
                break;
            case HealthKeys.PillarEngagement:
                var driver = pillar.Drivers.FirstOrDefault()?.Key ?? pillar.Evidence.GetValueOrDefault("driver");
                yield return driver switch
                {
                    HealthKeys.DriverFairTreatment => "Action_Engagement_Fairness",
                    HealthKeys.DriverComplaintsHandling => "Action_Engagement_Complaints",
                    HealthKeys.DriverBenefitsMatch => "Action_Engagement_Benefits",
                    HealthKeys.DriverWorkload => "Action_Engagement_Workload",
                    HealthKeys.DriverCommunication => "Action_Engagement_Communication",
                    HealthKeys.DriverEncourageOpinions => "Action_Engagement_Voice",
                    HealthKeys.DriverTeamwork => "Action_Engagement_Teamwork",
                    HealthKeys.DriverTaskFit => "Action_Engagement_TaskFit",
                    HealthKeys.DriverTraining => "Action_Engagement_Training",
                    HealthKeys.DriverUsePersonalAbilities => "Action_Engagement_Growth",
                    _ => "Action_Engagement_Generic",
                };
                break;
            case HealthKeys.PillarLeadership:
                yield return "Action_Leadership_Stability";
                break;
            case HealthKeys.PillarWorkforce:
                if (pillar.Evidence.ContainsKey("gap")) yield return "Action_Workforce_Staffing";
                yield return "Action_Workforce_AtRisk";
                break;
        }
    }

    private static string BuildEvidence(HealthPillarDto p) => p.Key switch
    {
        HealthKeys.PillarTurnover => $"{p.Evidence.GetValueOrDefault("value")}% vs {p.Evidence.GetValueOrDefault("baseline")}%",
        HealthKeys.PillarEarlyAttrition => $"{p.Evidence.GetValueOrDefault("value")}% vs {p.Evidence.GetValueOrDefault("baseline")}%",
        HealthKeys.PillarRetention => $"{p.Evidence.GetValueOrDefault("value")}% vs {p.Evidence.GetValueOrDefault("baseline")}%",
        HealthKeys.PillarEngagement => $"{p.Evidence.GetValueOrDefault("value")}% vs {p.Evidence.GetValueOrDefault("baseline")}%",
        HealthKeys.PillarLeadership => $"{p.Evidence.GetValueOrDefault("leaders")} / {p.Evidence.GetValueOrDefault("months")}m",
        HealthKeys.PillarWorkforce => p.Evidence.ContainsKey("gap") ? $"{p.Evidence.GetValueOrDefault("gap")} ({p.Evidence.GetValueOrDefault("gapPct")}%)" : $"{p.Evidence.GetValueOrDefault("atRisk")}",
        _ => "",
    };

    private static readonly string[] MonthAbbr =
        { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    private static string MonthLabel(int month, int year) =>
        month is >= 1 and <= 12 ? $"{MonthAbbr[month]} {year % 100:D2}" : $"{month}/{year}";
}
