using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class EarlyWarningService : IEarlyWarningService
{
    private readonly AppDbContext _db;
    public EarlyWarningService(AppDbContext db) => _db = db;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const int DefaultNewHireWindowDays = 90;
    private const int MinNewHireWindowDays     = 30;
    private const int MaxNewHireWindowDays     = 180;
    private const int PeakBucketWidthDays      = 30;
    private const int MinRateSampleSize        = 10;
    private const int MinPeakSampleSize        = 5;
    private const int MinExitSampleSize        = 3;
    /// <summary>Floor for StdDev to prevent over-sensitivity when data is homogeneous.</summary>
    private const double MinStdDev = 5.0;

    // ── Internal DTOs ─────────────────────────────────────────────────────────
    private class HistoricalRecord
    {
        public string Store    { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string Gender   { get; set; } = "";
        /// <summary>Null = still active (never resigned).</summary>
        public int? TenureDays { get; set; }
    }

    private class ActiveCandidate
    {
        public string   EmployeeId { get; set; } = "";
        public string   Name       { get; set; } = "";
        public string   Store      { get; set; } = "";
        public string   JobTitle   { get; set; } = "";
        public string   Gender     { get; set; } = "";
        public DateOnly HireDate   { get; set; }
        public int      TenureDays { get; set; }
    }

    // ── Data loaders ──────────────────────────────────────────────────────────
    private async Task<List<HistoricalRecord>> LoadHistoricalRecordsAsync()
    {
        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.JobTitle, e.Gender, e.HireDate })
            .ToListAsync();

        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.JobTitle, r.Gender, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, HistoricalRecord>();

        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new HistoricalRecord
            {
                Store = a.Store, JobTitle = a.JobTitle, Gender = a.Gender, TenureDays = null,
            };
        }

        // Resignation records win — they prove the employee actually left.
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new HistoricalRecord
            {
                Store      = r.Store,
                JobTitle   = r.JobTitle,
                Gender     = r.Gender,
                TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
            };
        }

        return byEmployee.Values.ToList();
    }

    private async Task<List<ActiveCandidate>> LoadActiveCandidatesAsync(string? months, int? year)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return [];

        (int Month, int Year) anchor;
        if (year.HasValue && !string.IsNullOrWhiteSpace(months))
        {
            var resolved = DashboardService.ResolvePeriods(null, year, null, null, months)
                .Where(p => periods.Any(x => x.Month == p.Month && x.Year == p.Year))
                .ToList();
            anchor = resolved.Count > 0
                ? resolved.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First()
                : periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Select(p => (p.Month, p.Year)).First();
        }
        else
        {
            anchor = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Select(p => (p.Month, p.Year)).First();
        }

        var asOf       = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1);
        var resignedIds = (await _db.Resignations.Select(r => r.EmployeeId).Distinct().ToListAsync()).ToHashSet();

        var rows = await _db.ActiveEmployees
            .Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Name, e.Store, e.JobTitle, e.Gender, e.HireDate })
            .ToListAsync();

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeId) && !resignedIds.Contains(r.EmployeeId))
            .Select(r => new ActiveCandidate
            {
                EmployeeId = r.EmployeeId,
                Name       = r.Name,
                Store      = r.Store,
                JobTitle   = r.JobTitle,
                Gender     = r.Gender,
                HireDate   = r.HireDate!.Value,
                TenureDays = asOf.DayNumber - r.HireDate!.Value.DayNumber,
            })
            .ToList();
    }

    // ── Statistical helpers ───────────────────────────────────────────────────

    private static double CalcStdDev(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;
        var mean = list.Average();
        return Math.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / list.Count);
    }

    /// <summary>
    /// Returns graduated score 1-3 based on how many effective StdDevs
    /// the <paramref name="rate"/> exceeds <paramref name="mean"/>.
    /// Returns 0 if not significant.
    /// </summary>
    private static int GraduatedScore(double rate, double mean, double stddev)
    {
        var eff = Math.Max(stddev, MinStdDev);
        if (rate >= mean + 2.0 * eff) return 3;
        if (rate >= mean + 1.5 * eff) return 2;
        if (rate >= mean + 1.0 * eff) return 1;
        return 0;
    }

    /// <summary>
    /// Groups historical records by <paramref name="keySelector"/>, computes early-leave
    /// rate per group, and returns the rate dictionary plus the distribution's mean and StdDev.
    /// Groups with fewer than <see cref="MinRateSampleSize"/> records are excluded.
    /// </summary>
    private static (Dictionary<string, double> Rates, double Mean, double Std) ComputeGroupRates(
        List<HistoricalRecord> historical,
        Func<HistoricalRecord, string> keySelector,
        int windowDays = DefaultNewHireWindowDays)
    {
        var valid = historical
            .Where(h => !string.IsNullOrWhiteSpace(keySelector(h)))
            .GroupBy(keySelector)
            .Select(g =>
            {
                var list = g.ToList();
                if (list.Count < MinRateSampleSize) return (Key: (string?)null, Rate: 0.0);
                var rate = list.Count(h => h.TenureDays != null && h.TenureDays <= windowDays) * 100.0 / list.Count;
                return (Key: g.Key, Rate: rate);
            })
            .Where(x => x.Key != null)
            .Select(x => (x.Key!, x.Rate))
            .ToList();

        var rates  = valid.ToDictionary(x => x.Item1, x => x.Item2);
        var allRates = valid.Select(x => x.Item2).ToList();
        return (rates, allRates.Count > 0 ? allRates.Average() : 0, CalcStdDev(allRates));
    }

    /// <summary>
    /// Computes a role-specific new-hire-window length from the median resignation tenure
    /// of employees in that role. Falls back to <see cref="DefaultNewHireWindowDays"/>.
    /// </summary>
    private static Dictionary<string, int> ComputeRoleWindows(List<HistoricalRecord> historical)
    {
        return historical
            .Where(h => h.TenureDays != null && !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .Where(g => g.Count() >= MinRateSampleSize)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var tenures = g.Where(h => h.TenureDays.HasValue)
                                   .Select(h => h.TenureDays!.Value)
                                   .OrderBy(x => x)
                                   .ToList();
                    if (tenures.Count == 0) return DefaultNewHireWindowDays;
                    var median = tenures[tenures.Count / 2];
                    return Math.Clamp(median, MinNewHireWindowDays, MaxNewHireWindowDays);
                });
    }

    /// <summary>
    /// Returns a score 1-3 for being inside the new-hire window;
    /// higher the closer the employee is to the end of the window.
    /// </summary>
    private static int NewHireWindowScore(int tenureDays, int windowDays)
    {
        if (tenureDays > windowDays) return 0;
        var progress = (double)tenureDays / windowDays;
        if (progress >= 0.75) return 3; // last 25 % — most at-risk
        if (progress >= 0.40) return 2; // mid stretch
        return 1;
    }

    private static (int Start, int End)? ComputePeakBucket(IEnumerable<int> lateTenures, int windowDays)
    {
        var list = lateTenures.ToList();
        if (list.Count < MinPeakSampleSize) return null;
        var best = list
            .GroupBy(t => (t - windowDays - 1) / PeakBucketWidthDays)
            .OrderByDescending(g => g.Count())
            .First();
        var start = windowDays + best.Key * PeakBucketWidthDays;
        return (start, start + PeakBucketWidthDays);
    }

    /// <summary>
    /// Arabic Likert/agree-disagree heuristic — same logic as ExitInterviewService.
    /// </summary>
    private static int ExitSentiment(string answer)
    {
        var a = answer.Trim();
        if (a.Contains("لا أوافق ولا أعارض") || a.Contains("محايد") || a == "مقبولة") return 0;
        if (a.Contains("أعارض") || a.Contains("ضعيف") || a == "لا")                  return -1;
        if (a.Contains("أوافق") || a == "جيدة" || a == "نعم"
            || a.Contains("كبيرة") || a.Contains("عالية"))                             return 1;
        return 0;
    }

    /// <summary>
    /// Computes negative-sentiment % per store from exit interviews.
    /// Considers five key engagement dimensions.
    /// </summary>
    private async Task<Dictionary<string, double>> LoadStoreExitScoresAsync()
    {
        var rows = await _db.ExitInterviews
            .Where(e => !string.IsNullOrEmpty(e.Store))
            .Select(e => new
            {
                e.Store,
                e.WorkloadCondition, e.FairTreatment,
                e.Communication,     e.Training,
                e.ComplaintsHandling,
            })
            .ToListAsync();

        return rows
            .GroupBy(e => e.Store)
            .Where(g => g.Count() >= MinExitSampleSize)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var answers = g.SelectMany(e => new[]
                    {
                        e.WorkloadCondition, e.FairTreatment,
                        e.Communication,     e.Training,
                        e.ComplaintsHandling,
                    })
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

                    if (answers.Count == 0) return 0.0;
                    return answers.Count(a => ExitSentiment(a) < 0) * 100.0 / answers.Count;
                });
    }

    /// <summary>
    /// For each active store, resolves the current store leader and computes their
    /// historical early-leave rate across all stores they have ever managed.
    /// </summary>
    private async Task<Dictionary<string, (string Leader, double LeaderRate)>> LoadStoreLeaderRatesAsync(
        List<HistoricalRecord> historical)
    {
        var storeRefs = await _db.StoreReferences
            .Where(s => !string.IsNullOrEmpty(s.StoreLeader) && !string.IsNullOrEmpty(s.StoreName))
            .Select(s => new { s.StoreName, s.StoreLeader, s.Year, s.Month })
            .ToListAsync();

        if (storeRefs.Count == 0) return [];

        // Most recent leader per store
        var currentLeaders = storeRefs
            .GroupBy(s => s.StoreName)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First().StoreLeader);

        // All stores each leader has ever managed
        var leaderStores = storeRefs
            .GroupBy(s => s.StoreLeader)
            .ToDictionary(g => g.Key, g => g.Select(s => s.StoreName).Distinct().ToHashSet());

        // Historical resignation stats per store
        var histByStore = historical
            .Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .ToDictionary(g => g.Key, g => (
                Total:      g.Count(),
                EarlyLeave: g.Count(h => h.TenureDays != null && h.TenureDays <= DefaultNewHireWindowDays)));

        // Weighted early-leave rate per leader across all their stores
        var leaderRates = new Dictionary<string, double>();
        foreach (var (leader, stores) in leaderStores)
        {
            int total = 0, earlyLeave = 0;
            foreach (var s in stores)
                if (histByStore.TryGetValue(s, out var hr)) { total += hr.Total; earlyLeave += hr.EarlyLeave; }
            if (total >= MinRateSampleSize)
                leaderRates[leader] = earlyLeave * 100.0 / total;
        }

        // Map store → (leader, rate)
        var result = new Dictionary<string, (string, double)>();
        foreach (var (store, leader) in currentLeaders)
            if (leaderRates.TryGetValue(leader, out var rate))
                result[store] = (leader, rate);

        return result;
    }

    /// <summary>Maps a raw cumulative score to a 1-5 star display value.</summary>
    private static int ScoreToStars(int score) => score switch
    {
        <= 2  => 1,
        <= 4  => 2,
        <= 6  => 3,
        <= 9  => 4,
        _     => 5,
    };

    // ── Public API ────────────────────────────────────────────────────────────
    public async Task<List<string>> GetStoreListAsync()
    {
        var candidates = await LoadActiveCandidatesAsync(null, null);
        return candidates.Select(c => c.Store)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    public async Task<List<EarlyWarningItem>> GetWatchlistAsync(
        string? store, string? months = null, int? year = null)
    {
        // ── Load raw data ─────────────────────────────────────────────────────
        var historical = await LoadHistoricalRecordsAsync();
        var candidates = await LoadActiveCandidatesAsync(months, year);
        if (!string.IsNullOrWhiteSpace(store))
            candidates = candidates.Where(c => c.Store == store).ToList();

        // ── Company-wide baseline ─────────────────────────────────────────────
        var companyTotal    = historical.Count;
        var companyEarly    = historical.Count(h => h.TenureDays != null && h.TenureDays <= DefaultNewHireWindowDays);
        var companyRate     = companyTotal > 0 ? companyEarly * 100.0 / companyTotal : 0;

        // ── Role-specific new-hire windows ────────────────────────────────────
        var roleWindows = ComputeRoleWindows(historical);

        // ── Rate distributions with StdDev thresholds ────────────────────────
        var (storeRates,  storeMean,  storeStd)  = ComputeGroupRates(historical, h => h.Store);
        var (roleRates,   roleMean,   roleStd)   = ComputeGroupRates(historical, h => h.JobTitle);
        var (genderRates, genderMean, genderStd) = ComputeGroupRates(historical, h => h.Gender);

        // ── Peak windows (store → role → company fallback) ────────────────────
        var lateHistorical = historical
            .Where(h => h.TenureDays != null && h.TenureDays > DefaultNewHireWindowDays)
            .ToList();
        var companyPeakBucket = ComputePeakBucket(lateHistorical.Select(h => h.TenureDays!.Value), DefaultNewHireWindowDays);
        var storePeakBuckets  = lateHistorical.Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .ToDictionary(g => g.Key, g => ComputePeakBucket(g.Select(h => h.TenureDays!.Value), DefaultNewHireWindowDays));
        var rolePeakBuckets   = lateHistorical.Where(h => !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .ToDictionary(g => g.Key, g => ComputePeakBucket(g.Select(h => h.TenureDays!.Value), DefaultNewHireWindowDays));

        // ── Exit interview store health ───────────────────────────────────────
        var exitScores  = await LoadStoreExitScoresAsync();
        var exitMean    = exitScores.Count > 0 ? exitScores.Values.Average() : 0;
        var exitStd     = Math.Max(CalcStdDev(exitScores.Values), MinStdDev);

        // ── Store leader history ──────────────────────────────────────────────
        var leaderRates     = await LoadStoreLeaderRatesAsync(historical);
        var allLeaderRates  = leaderRates.Values.Select(x => x.LeaderRate).ToList();
        var leaderMean      = allLeaderRates.Count > 0 ? allLeaderRates.Average() : companyRate;
        var leaderStd       = Math.Max(CalcStdDev(allLeaderRates), MinStdDev);

        // ── Score each active employee ────────────────────────────────────────
        var result = new List<EarlyWarningItem>();

        foreach (var c in candidates)
        {
            var reasons    = new List<EarlyWarningReason>();
            var roleWindow = roleWindows.TryGetValue(c.JobTitle, out var rw) ? rw : DefaultNewHireWindowDays;

            // 1 ── New Hire Window (graduated 1-3, role-specific window)
            var nhScore = NewHireWindowScore(c.TenureDays, roleWindow);
            if (nhScore > 0)
                reasons.Add(new EarlyWarningReason
                {
                    Type  = "new_hire_window",
                    Score = nhScore,
                    Params = new() { ["days"] = c.TenureDays.ToString(), ["window"] = roleWindow.ToString() },
                });

            // 2 ── Store History (graduated 1-3)
            if (storeRates.TryGetValue(c.Store, out var sRate))
            {
                var sScore = GraduatedScore(sRate, storeMean, storeStd);
                if (sScore > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "store_history",
                        Score = sScore,
                        Params = new() { ["store"] = c.Store, ["rate"] = sRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                    });
            }

            // 3 ── Role History (graduated 1-3)
            if (roleRates.TryGetValue(c.JobTitle, out var rRate))
            {
                var rScore = GraduatedScore(rRate, roleMean, roleStd);
                if (rScore > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "role_history",
                        Score = rScore,
                        Params = new() { ["jobTitle"] = c.JobTitle, ["rate"] = rRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                    });
            }

            // 4 ── Peak Resignation Window (1 = approaching, 2 = inside)
            var peakBucket = (storePeakBuckets.TryGetValue(c.Store, out var sb) ? sb : null)
                          ?? (rolePeakBuckets.TryGetValue(c.JobTitle, out var rb) ? rb : null)
                          ?? companyPeakBucket;
            if (peakBucket.HasValue && c.TenureDays > DefaultNewHireWindowDays)
            {
                var inCore    = c.TenureDays >= peakBucket.Value.Start && c.TenureDays <= peakBucket.Value.End;
                var approach  = c.TenureDays >= peakBucket.Value.Start - PeakBucketWidthDays
                             && c.TenureDays <  peakBucket.Value.Start;
                if (inCore || approach)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "peak_window",
                        Score = inCore ? 2 : 1,
                        Params = new() { ["start"] = peakBucket.Value.Start.ToString(), ["end"] = peakBucket.Value.End.ToString() },
                    });
            }

            // 5 ── Gender History (1 point if gender group is significantly high-risk)
            if (!string.IsNullOrWhiteSpace(c.Gender) && genderRates.TryGetValue(c.Gender, out var gRate))
            {
                if (GraduatedScore(gRate, genderMean, genderStd) > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "gender_history",
                        Score = 1,
                        Params = new() { ["gender"] = c.Gender, ["rate"] = gRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                    });
            }

            // 6 ── Exit Interview Store Score (1-2)
            if (exitScores.TryGetValue(c.Store, out var negRate) && negRate > 0)
            {
                var eiScore = GraduatedScore(negRate, exitMean, exitStd) >= 2 ? 2
                            : GraduatedScore(negRate, exitMean, exitStd) == 1 ? 1
                            : 0;
                if (eiScore > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "exit_interview_score",
                        Score = eiScore,
                        Params = new() { ["store"] = c.Store, ["negRate"] = negRate.ToString("F0") },
                    });
            }

            // 7 ── Store Leader History (1 point)
            if (leaderRates.TryGetValue(c.Store, out var li)
                && GraduatedScore(li.LeaderRate, leaderMean, leaderStd) > 0)
                reasons.Add(new EarlyWarningReason
                {
                    Type  = "store_leader_history",
                    Score = 1,
                    Params = new() { ["leader"] = li.Leader, ["rate"] = li.LeaderRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                });

            if (reasons.Count == 0) continue;

            var totalScore = reasons.Sum(r => r.Score);
            result.Add(new EarlyWarningItem
            {
                Name      = c.Name,
                Store     = c.Store,
                JobTitle  = c.JobTitle,
                HireDate  = c.HireDate,
                TenureDays = c.TenureDays,
                RiskScore = totalScore,
                Stars     = ScoreToStars(totalScore),
                Reasons   = reasons,
            });
        }

        return result.OrderByDescending(r => r.RiskScore).ThenBy(r => r.TenureDays).ToList();
    }

    public async Task<EarlyWarningSummary> GetSummaryAsync(
        string? store, string? months = null, int? year = null)
    {
        var historical   = await LoadHistoricalRecordsAsync();
        var companyTotal = historical.Count;
        var companyEarly = historical.Count(h => h.TenureDays != null && h.TenureDays <= DefaultNewHireWindowDays);
        var companyRate  = companyTotal > 0 ? companyEarly * 100.0 / companyTotal : 0;

        var list = await GetWatchlistAsync(store, months, year);
        return new EarlyWarningSummary
        {
            TotalWatchlist      = list.Count,
            HighRiskCount       = list.Count(r => r.Stars >= 4),
            NewHireWindowCount  = list.Count(r => r.Reasons.Any(x => x.Type == "new_hire_window")),
            CompanyBaselineRate = Math.Round(companyRate, 1),
        };
    }
}
