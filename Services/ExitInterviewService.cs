using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ExitInterviewService : IExitInterviewService
{
    private readonly AppDbContext _db;

    public ExitInterviewService(AppDbContext db) => _db = db;

    private static IQueryable<ExitInterview> ApplyFilter(IQueryable<ExitInterview> q, ExitInterviewFilter filter, string role, string? assignedName)
    {
        // Same store-scoping convention as DashboardService.GetAccessibleStoresAsync:
        // Admin_Full/Admin_Read see everything, OC/OM only see their own stores.
        if (role == "Operation_Manager" && !string.IsNullOrEmpty(assignedName))
            q = q.Where(e => e.OperationManager == assignedName);
        else if (role == "Operation_Consultant" && !string.IsNullOrEmpty(assignedName))
            q = q.Where(e => e.OperationConsultant == assignedName);

        if (!string.IsNullOrWhiteSpace(filter.Store)) q = q.Where(e => e.Store == filter.Store);
        if (!string.IsNullOrWhiteSpace(filter.StoreLeader)) q = q.Where(e => e.StoreLeader == filter.StoreLeader);
        if (!string.IsNullOrWhiteSpace(filter.OperationConsultant)) q = q.Where(e => e.OperationConsultant == filter.OperationConsultant);
        if (!string.IsNullOrWhiteSpace(filter.OperationManager)) q = q.Where(e => e.OperationManager == filter.OperationManager);
        return q;
    }

    private Task<List<ExitInterview>> FilteredAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        ApplyFilter(_db.ExitInterviews.AsNoTracking(), filter, role, assignedName).ToListAsync();

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

    public async Task<ExitInterviewFilterOptions> GetFilterOptionsAsync(string role, string? assignedName)
    {
        var rows = await ApplyFilter(_db.ExitInterviews.AsNoTracking(), new ExitInterviewFilter(), role, assignedName)
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
        GroupCount((await FilteredAsync(filter, role, assignedName)).Select(e => e.ReasonForLeaving));

    public async Task<List<ChartDataItem>> GetWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount((await FilteredAsync(filter, role, assignedName)).Select(e => e.WouldReturn));

    public async Task<List<ChartDataItem>> GetOverallExperienceAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount((await FilteredAsync(filter, role, assignedName)).Select(e => e.OverallExperience));

    public async Task<List<ChartDataItem>> GetWorkloadConditionAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        GroupCount((await FilteredAsync(filter, role, assignedName)).Select(e => e.WorkloadCondition));

    public async Task<List<EngagementDriverItem>> GetEngagementDriversAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        var rows = await FilteredAsync(filter, role, assignedName);

        var drivers = new (string Label, Func<ExitInterview, string> Selector)[]
        {
            ("Fair Treatment", e => e.FairTreatment),
            ("Encouraged to Share Opinions", e => e.EncourageOpinions),
            ("Complaints Handled Effectively", e => e.ComplaintsHandling),
            ("Benefits Match Job Requirements", e => e.BenefitsMatch),
            ("Teamwork & Collaboration", e => e.Teamwork),
            ("Communication with Management", e => e.Communication),
            ("Assigned Appropriate Tasks", e => e.TaskFit),
            ("Adequate Training", e => e.Training),
            ("Received Feedback & Guidance", e => e.Feedback),
            ("Could Use Personal Abilities", e => e.UsePersonalAbilities),
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

    public async Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName)
    {
        var rows = await FilteredAsync(filter, role, assignedName);
        var result = new List<ExitInterviewCommentItem>();

        void AddIfPresent(ExitInterview e, string? text, string questionLabel)
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
            AddIfPresent(e, e.ReasonOtherText, "Other Reason");
            AddIfPresent(e, e.WorkPressureReasonText, "Workload Pressure Reason");
            AddIfPresent(e, e.WhatWouldChangeText, "What They'd Change");
            AddIfPresent(e, e.WhatLearnedText, "What They Learned");
            AddIfPresent(e, e.FinalCommentsText, "Final Comments");
        }

        return result.OrderByDescending(c => c.SubmittedAt).ToList();
    }
}
