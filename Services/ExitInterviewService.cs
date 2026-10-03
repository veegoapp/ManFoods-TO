using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;

namespace MvcApp.Services;

public class ExitInterviewService : IExitInterviewService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IStringLocalizer<SharedResource> _L;

    public ExitInterviewService(AppDbContext db, IStoreAccessService storeAccess, IStringLocalizer<SharedResource> localizer)
    {
        _db = db;
        _storeAccess = storeAccess;
        _L = localizer;
    }

    private async Task<IQueryable<ExitInterview>> ApplyFilterAsync(IQueryable<ExitInterview> q, ExitInterviewFilter filter, string role, string? assignedName)
    {
        // The Store is the access boundary: a restricted role sees every exit
        // interview for a store it currently owns (per the latest Store
        // Reference upload), regardless of who managed that store when the
        // interview happened. "assignedName" here is actually the logged-in
        // user's email (see HttpContext.Session.GetEmail() at call sites).
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));

        if (MultiValueFilter.Split(filter.Store) is { } stores) q = q.Where(e => stores.Contains(e.Store));
        if (!string.IsNullOrWhiteSpace(filter.StoreLeader)) q = q.Where(e => e.StoreLeader == filter.StoreLeader);
        if (MultiValueFilter.Split(filter.OperationConsultant) is { } ocs) q = q.Where(e => ocs.Contains(e.OperationConsultant));
        if (MultiValueFilter.Split(filter.OperationManager) is { } oms) q = q.Where(e => oms.Contains(e.OperationManager));
        if (!string.IsNullOrWhiteSpace(filter.SeniorOperationConsultant) || !string.IsNullOrWhiteSpace(filter.OperationDirector))
        {
            // ExitInterview stores OC/OM snapshots but not SOC/OD. Resolve those
            // dimensions through each store's latest reference so comparison
            // charts still honor the leadership filters selected by the user.
            var latestReferences = await _db.StoreReferences
                .AsNoTracking()
                .Select(s => new
                {
                    s.StoreName,
                    s.SeniorOperationConsultant,
                    s.OperationDirector,
                    s.Year,
                    s.Month
                })
                .ToListAsync();
            var latestByStore = latestReferences
                .GroupBy(s => s.StoreName)
                .ToDictionary(g => g.Key, g => g
                    .OrderByDescending(s => s.Year)
                    .ThenByDescending(s => s.Month)
                    .First());
            var matchingStores = latestByStore.Values
                .Where(s => (string.IsNullOrWhiteSpace(filter.SeniorOperationConsultant)
                             || MultiValueFilter.Split(filter.SeniorOperationConsultant)!.Contains(s.SeniorOperationConsultant))
                         && (string.IsNullOrWhiteSpace(filter.OperationDirector)
                             || MultiValueFilter.Split(filter.OperationDirector)!.Contains(s.OperationDirector)))
                .Select(s => s.StoreName)
                .ToList();
            q = q.Where(e => matchingStores.Contains(e.Store));
        }
        if (MultiValueFilter.Split(filter.Jobs) is { } jobs) q = q.Where(e => jobs.Contains(e.JobTitle));
        // Year=0 is the synthetic "undated" sentinel — skip date filtering so
        // all rows (which have month=0/year=0) are returned unfiltered.
        if (filter.Year.HasValue && filter.Year.Value > 0)
        {
            var periods = DashboardService.ResolvePeriods(null, filter.Year, null, null, filter.Months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
            q = q.Where(e => keys.Contains(e.Year * 100 + e.Month));
        }
        return q;
    }

    // Projects to only the column(s) each caller actually needs, instead of
    // materializing the full (wide, free-text-heavy) ExitInterview entity every
    // time — same filtered row set and row order as before, just fewer columns
    // crossing the wire.
    private async Task<List<TResult>> FilteredAsync<TResult>(
        ExitInterviewFilter filter, string role, string? assignedName, Expression<Func<ExitInterview, TResult>> selector) =>
        await (await ApplyFilterAsync(_db.ExitInterviews.AsNoTracking(), filter, role, assignedName))
            .Select(selector)
            .ToListAsync();

    private static List<ChartDataItem> GroupCount(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .GroupBy(v => v)
              .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
              .OrderByDescending(c => c.Value)
              .ToList();

    /// <summary>
    /// Best-effort Arabic Likert/agree-disagree sentiment heuristic. The
    /// neutral phrase is checked first because it contains "أوافق" as a
    /// substring, which would otherwise be misread as agreement.
    /// </summary>
    private static int Sentiment(string answer)
    {
        var a = answer.Trim();
        if (a.Length == 0) return 0;
        if (a.Contains("لا أوافق ولا أعارض") || a.Contains("محايد") || a == "مقبولة") return 0;
        if (a.Contains("أعارض") || a.Contains("ضعيف") || a == "لا") return -1;
        if (a.Contains("أوافق") || a == "جيدة" || a == "نعم" || a.Contains("كبيرة") || a.Contains("عالية")) return 1;
        return 0;
    }

    public async Task<List<PeriodItem>> GetAvailablePeriodsAsync()
    {
        var hasAny = await _db.ExitInterviews.AnyAsync();
        if (!hasAny) return new List<PeriodItem>();

        // Return real dated periods first; if all rows lack dates (month/year=0
        // because the Forms export column was unrecognised), return a synthetic
        // period {0,0} so the page still shows the data without a date filter.
        var periods = await _db.ExitInterviews
            .Where(e => e.Month > 0 && e.Year > 0)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PeriodItem { Month = p.Month, Year = p.Year })
            .ToListAsync();

        // Add sentinel {0,0} whenever any rows lack a proper date (month=0 or
        // year=0) — covers both all-undated and mixed dated/undated datasets.
        var hasUndated = await _db.ExitInterviews.AnyAsync(e => e.Month == 0 || e.Year == 0);
        if (hasUndated)
            periods.Add(new PeriodItem { Month = 0, Year = 0 });

        return periods;
    }

    public async Task<ExitInterviewFilterOptions> GetFilterOptionsAsync(string role, string? assignedName)
    {
        var rows = await (await ApplyFilterAsync(_db.ExitInterviews.AsNoTracking(), new ExitInterviewFilter(), role, assignedName))
            .Select(e => new { e.Store, e.StoreLeader, e.OperationConsultant, e.OperationManager })
            .ToListAsync();

        static List<string> Distinct(IEnumerable<string> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v).ToList();

        return new ExitInterviewFilterOptions
        {
            Stores = Distinct(rows.Select(r => r.Store)),
            StoreLeaders = Distinct(rows.Select(r => r.StoreLeader)),
            OperationConsultants = Distinct(rows.Select(r => r.OperationConsultant)),
            OperationManagers = Distinct(rows.Select(r => r.OperationManager)),
        };
    }

    public async Task<List<ChartDataItem>> GetReasonsForLeavingAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.ReasonForLeaving));

    public async Task<List<ChartDataItem>> GetWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.WouldReturn));

    public async Task<List<ChartDataItem>> GetOverallExperienceAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.OverallExperience));

    public async Task<List<ChartDataItem>> GetWorkloadConditionAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.WorkloadCondition));

    public async Task<List<ChartDataItem>> GetTrainingAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.Training));

    public async Task<List<ChartDataItem>> GetFairTreatmentAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.FairTreatment));

    public async Task<List<ChartDataItem>> GetWorkPressureReasonAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.WorkPressureReasonText ?? ""));

    /// <summary>Per-store negativity for every engagement driver, keyed stably
    /// (HealthKeys.Driver*). "Negative" uses the same Sentiment() mapping as the
    /// rest of this service (answer classified as -1). Only stores with at least
    /// one exit response appear.</summary>
    public async Task<Dictionary<string, List<HealthDriverDto>>> GetStoreEngagementProfilesAsync(string role, string? assignedName)
    {
        var rows = await (await ApplyFilterAsync(_db.ExitInterviews.AsNoTracking(), new ExitInterviewFilter(), role, assignedName))
            .Select(e => new StoreDriverRow
            {
                Store = e.Store,
                FairTreatment = e.FairTreatment, ComplaintsHandling = e.ComplaintsHandling,
                BenefitsMatch = e.BenefitsMatch, WorkloadCondition = e.WorkloadCondition,
                Communication = e.Communication, EncourageOpinions = e.EncourageOpinions,
                Teamwork = e.Teamwork, TaskFit = e.TaskFit, Training = e.Training,
                UsePersonalAbilities = e.UsePersonalAbilities,
            })
            .ToListAsync();

        var drivers = new (string Key, Func<StoreDriverRow, string> Selector)[]
        {
            (HealthKeys.DriverFairTreatment,       e => e.FairTreatment),
            (HealthKeys.DriverComplaintsHandling,  e => e.ComplaintsHandling),
            (HealthKeys.DriverBenefitsMatch,       e => e.BenefitsMatch),
            (HealthKeys.DriverWorkload,            e => e.WorkloadCondition),
            (HealthKeys.DriverCommunication,       e => e.Communication),
            (HealthKeys.DriverEncourageOpinions,   e => e.EncourageOpinions),
            (HealthKeys.DriverTeamwork,            e => e.Teamwork),
            (HealthKeys.DriverTaskFit,             e => e.TaskFit),
            (HealthKeys.DriverTraining,            e => e.Training),
            (HealthKeys.DriverUsePersonalAbilities, e => e.UsePersonalAbilities),
        };

        var result = new Dictionary<string, List<HealthDriverDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var storeGroup in rows.Where(r => !string.IsNullOrWhiteSpace(r.Store)).GroupBy(r => r.Store))
        {
            var list = new List<HealthDriverDto>();
            foreach (var (key, selector) in drivers)
            {
                var answers = storeGroup.Select(r => selector(r)).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
                if (answers.Count == 0) continue;
                var negatives = answers.Count(a => Sentiment(a) < 0);
                list.Add(new HealthDriverDto
                {
                    Key = key,
                    NegativePercent = Math.Round(negatives * 100.0 / answers.Count, 1),
                    Responses = answers.Count,
                });
            }
            if (list.Count > 0) result[storeGroup.Key] = list;
        }
        return result;
    }

    private class EngagementDriverRow
    {
        public string FairTreatment { get; set; } = "";
        public string EncourageOpinions { get; set; } = "";
        public string ComplaintsHandling { get; set; } = "";
        public string BenefitsMatch { get; set; } = "";
        public string Teamwork { get; set; } = "";
        public string Communication { get; set; } = "";
        public string TaskFit { get; set; } = "";
        public string Training { get; set; } = "";
        public string Feedback { get; set; } = "";
        public string UsePersonalAbilities { get; set; } = "";
    }

    private class StoreDriverRow
    {
        public string Store { get; set; } = "";
        public string FairTreatment { get; set; } = "";
        public string ComplaintsHandling { get; set; } = "";
        public string BenefitsMatch { get; set; } = "";
        public string WorkloadCondition { get; set; } = "";
        public string Communication { get; set; } = "";
        public string EncourageOpinions { get; set; } = "";
        public string Teamwork { get; set; } = "";
        public string TaskFit { get; set; } = "";
        public string Training { get; set; } = "";
        public string UsePersonalAbilities { get; set; } = "";
    }

    public async Task<List<EngagementDriverItem>> GetEngagementDriversAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        var rows = await FilteredAsync(filter, role, assignedName, e => new EngagementDriverRow
        {
            FairTreatment = e.FairTreatment,
            EncourageOpinions = e.EncourageOpinions,
            ComplaintsHandling = e.ComplaintsHandling,
            BenefitsMatch = e.BenefitsMatch,
            Teamwork = e.Teamwork,
            Communication = e.Communication,
            TaskFit = e.TaskFit,
            Training = e.Training,
            Feedback = e.Feedback,
            UsePersonalAbilities = e.UsePersonalAbilities,
        });

        var drivers = new (string Label, Func<EngagementDriverRow, string> Selector)[]
        {
            (_L["EiDriver_FairTreatment"], e => e.FairTreatment),
            (_L["EiDriver_EncourageOpinions"], e => e.EncourageOpinions),
            (_L["EiDriver_ComplaintsHandling"], e => e.ComplaintsHandling),
            (_L["EiDriver_BenefitsMatch"], e => e.BenefitsMatch),
            (_L["EiDriver_Teamwork"], e => e.Teamwork),
            (_L["EiDriver_Communication"], e => e.Communication),
            (_L["EiDriver_TaskFit"], e => e.TaskFit),
            (_L["EiDriver_Training"], e => e.Training),
            (_L["EiDriver_Feedback"], e => e.Feedback),
            (_L["EiDriver_UsePersonalAbilities"], e => e.UsePersonalAbilities),
        };

        var result = new List<EngagementDriverItem>();
        foreach (var (label, selector) in drivers)
        {
            var answers = rows.Select(selector).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            var positivePercent = answers.Count == 0
                ? 0
                : Math.Round(answers.Count(a => Sentiment(a) > 0) * 100.0 / answers.Count, 1);
            result.Add(new EngagementDriverItem { Label = label, PositivePercent = positivePercent, TotalResponses = answers.Count });
        }
        return result.OrderBy(d => d.PositivePercent).ToList();
    }

    public async Task<ExitSentimentSummary> GetSentimentSummaryAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        var rows = await FilteredAsync(filter, role, assignedName, e => new { e.WouldReturn, e.OverallExperience });
        // Sentiment is derived from WouldReturn + OverallExperience, but
        // TotalResponses must count forms (rows), not the sum of two answer columns.
        var answers = rows.Select(e => e.WouldReturn).Concat(rows.Select(e => e.OverallExperience))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();

        return new ExitSentimentSummary
        {
            TotalResponses = rows.Count,
            PositivePercent = answers.Count == 0 ? 0 : Math.Round(answers.Count(a => Sentiment(a) > 0) * 100.0 / answers.Count, 1),
        };
    }

    private class SentimentSourceRow
    {
        public string Name { get; set; } = "";
        public string WouldReturn { get; set; } = "";
        public string OverallExperience { get; set; } = "";
    }

    public async Task<Dictionary<string, ExitSentimentSummary>> GetSentimentSummariesByDimensionAsync(
        string dimension, IReadOnlyCollection<string> names, string role, string? assignedName)
    {
        if (names.Count == 0) return new Dictionary<string, ExitSentimentSummary>();

        if (dimension != "leader" && dimension != "oc" && dimension != "om")
        {
            // Every other dimension (e.g. "soc"/"od") looks up sentiment with an
            // unfiltered ExitInterviewFilter() regardless of name — the same call
            // GetSentimentSummaryAsync(new ExitInterviewFilter(), ...) would make
            // for every single name — so compute it once and reuse for all of them.
            var overall = await GetSentimentSummaryAsync(new ExitInterviewFilter(), role, assignedName);
            return names.ToDictionary(n => n, _ => overall);
        }

        var nameSet = names.ToHashSet();
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        IQueryable<ExitInterview> q = _db.ExitInterviews.AsNoTracking();
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));

        var rows = dimension switch
        {
            "leader" => await q.Where(e => nameSet.Contains(e.StoreLeader))
                .Select(e => new SentimentSourceRow { Name = e.StoreLeader, WouldReturn = e.WouldReturn, OverallExperience = e.OverallExperience })
                .ToListAsync(),
            "oc" => await q.Where(e => nameSet.Contains(e.OperationConsultant))
                .Select(e => new SentimentSourceRow { Name = e.OperationConsultant, WouldReturn = e.WouldReturn, OverallExperience = e.OverallExperience })
                .ToListAsync(),
            _ => await q.Where(e => nameSet.Contains(e.OperationManager))
                .Select(e => new SentimentSourceRow { Name = e.OperationManager, WouldReturn = e.WouldReturn, OverallExperience = e.OverallExperience })
                .ToListAsync(),
        };

        var result = rows.GroupBy(r => r.Name).ToDictionary(g => g.Key, g =>
        {
            var groupRows = g.ToList();
            // Same "TotalResponses counts forms, not the sum of two answer columns"
            // rule as GetSentimentSummaryAsync.
            var answers = groupRows.Select(r => r.WouldReturn).Concat(groupRows.Select(r => r.OverallExperience))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToList();
            return new ExitSentimentSummary
            {
                TotalResponses = groupRows.Count,
                PositivePercent = answers.Count == 0 ? 0 : Math.Round(answers.Count(a => Sentiment(a) > 0) * 100.0 / answers.Count, 1),
            };
        });

        // A name with zero matching exit interviews gets the same empty summary
        // GetSentimentSummaryAsync would have returned for it.
        foreach (var name in nameSet)
            if (!result.ContainsKey(name))
                result[name] = new ExitSentimentSummary();

        return result;
    }

    private class CommentRow
    {
        public string Store { get; set; } = "";
        public string StoreLeader { get; set; } = "";
        public string? ReasonOtherText { get; set; }
        public string? WorkPressureReasonText { get; set; }
        public string? WhatWouldChangeText { get; set; }
        public string? WhatLearnedText { get; set; }
        public string? FinalCommentsText { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }

    public async Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        var rows = await FilteredAsync(filter, role, assignedName, e => new CommentRow
        {
            Store = e.Store,
            StoreLeader = e.StoreLeader,
            ReasonOtherText = e.ReasonOtherText,
            WorkPressureReasonText = e.WorkPressureReasonText,
            WhatWouldChangeText = e.WhatWouldChangeText,
            WhatLearnedText = e.WhatLearnedText,
            FinalCommentsText = e.FinalCommentsText,
            SubmittedAt = e.SubmittedAt,
        });
        var result = new List<ExitInterviewCommentItem>();

        void AddIfPresent(CommentRow e, string? text, string questionLabel)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            result.Add(new ExitInterviewCommentItem
            {
                Store = e.Store,
                StoreLeader = e.StoreLeader,
                QuestionLabel = questionLabel,
                Text = text!.Trim(),
                SubmittedAt = e.SubmittedAt,
            });
        }

        foreach (var e in rows)
        {
            // A comment without its store and store-leader context is an
            // incomplete exit-interview row. Exclude it at the service boundary
            // so the page table and the Excel comments sheet stay consistent.
            if (string.IsNullOrWhiteSpace(e.Store) || string.IsNullOrWhiteSpace(e.StoreLeader))
                continue;

            AddIfPresent(e, e.ReasonOtherText, "Other Reason");
            AddIfPresent(e, e.WorkPressureReasonText, "Workload Pressure Reason");
            AddIfPresent(e, e.WhatWouldChangeText, "What They'd Change");
            AddIfPresent(e, e.WhatLearnedText, "What They Learned");
            AddIfPresent(e, e.FinalCommentsText, "Final Comments");
        }

        return result.OrderByDescending(c => c.SubmittedAt).ToList();
    }

    public async Task<List<ChartDataItem>> GetByJobTitleAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount(await FilteredAsync(filter, role, assignedName, e => e.JobTitle));

    private static bool IsYes(string answer)
    {
        var a = answer.Trim().ToLowerInvariant();
        return a is "yes" or "y" or "true" or "نعم";
    }

    public async Task<List<ExitReasonTrendPoint>> GetReasonsTrendAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        // Always full history — same rule as other trend charts elsewhere in the
        // app — so the filter's own Year/Months selection is dropped here while
        // the store/leader/OC/OM scoping is kept.
        var allTimeFilter = new ExitInterviewFilter
        {
            Store = filter.Store, StoreLeader = filter.StoreLeader,
            OperationConsultant = filter.OperationConsultant, OperationManager = filter.OperationManager,
        };
        var rows = await FilteredAsync(allTimeFilter, role, assignedName, e => new { e.Month, e.Year, e.ReasonForLeaving });
        var dated = rows.Where(r => r.Month > 0 && r.Year > 0 && !string.IsNullOrWhiteSpace(r.ReasonForLeaving)).ToList();
        if (dated.Count == 0) return new List<ExitReasonTrendPoint>();

        var topReasons = dated.GroupBy(r => r.ReasonForLeaving)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        return dated.GroupBy(r => (r.Year, r.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new ExitReasonTrendPoint
            {
                Label = new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMM yy"),
                Counts = topReasons.ToDictionary(reason => reason, reason => g.Count(r => r.ReasonForLeaving == reason)),
            })
            .ToList();
    }

    public async Task<List<ExitReasonReturnItem>> GetReasonVsWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        var rows = await FilteredAsync(filter, role, assignedName, e => new { e.ReasonForLeaving, e.WouldReturn });
        var valid = rows.Where(r => !string.IsNullOrWhiteSpace(r.ReasonForLeaving)).ToList();

        return valid.GroupBy(r => r.ReasonForLeaving)
            .Select(g =>
            {
                var withAnswer = g.Where(r => !string.IsNullOrWhiteSpace(r.WouldReturn)).ToList();
                var yesCount = withAnswer.Count(r => IsYes(r.WouldReturn));
                return new ExitReasonReturnItem
                {
                    Reason = g.Key,
                    Count = g.Count(),
                    WouldReturnPercent = withAnswer.Count > 0 ? Math.Round(yesCount * 100.0 / withAnswer.Count, 1) : 0,
                };
            })
            .Where(x => x.Count >= 2)
            .OrderByDescending(x => x.Count)
            .Take(6)
            .ToList();
    }
}
