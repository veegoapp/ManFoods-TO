using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Detects turnover problems per store from the existing analytics services,
/// creates/updates a single active StoreActionPlan per store with fixed-template
/// recommendations, and auto-resolves it once healthy for 2 consecutive monthly
/// cycles. No tasks, no approvals — managers only add notes on top of what the
/// system already generated.
/// </summary>
public class StoreActionPlanService : IStoreActionPlanService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IActionPlanRoleService _actionPlanRoles;
    private readonly IActionPlanSeverityConfigService _severityConfig;
    private readonly IStoreService _stores;
    private readonly IDashboardService _dashboard;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IRetentionService _retention;
    private readonly IEarlyWarningService _earlyWarning;
    private readonly IExitInterviewService _exitInterview;
    private readonly IRecommendationTemplateService _templates;

    // Detection runs system-wide across every store regardless of who's logged
    // in, so it always calls the underlying analytics services unrestricted.
    private const string SystemRole = "Admin";

    // ── Signal thresholds — first-pass defaults, tune after real data review ──
    private const int MinHeadcountForRateSignals = 5;
    private const double OverallTurnoverRateThreshold = 15.0;
    private const int MinHiresForEarlyLeaverSignal = 3;
    private const double EarlyLeaverRateThreshold = 30.0;
    private const int MinHiresForRetentionSignal = 3;
    private const double SixMonthRetentionThreshold = 70.0;
    private const int MinExitResponsesForSentimentSignal = 2;
    private const double ExitSentimentPositiveThreshold = 50.0;
    private const int MinExitResponsesForReasonSignal = 2;
    private const double ReasonConcentrationShareThreshold = 0.5;
    private const int LeadershipChangeLookbackMonths = 6;
    private const int LeadershipChangeCountThreshold = 2;
    private const int EarlyWarningHighRiskThreshold = 2;
    private const int HealthyCyclesRequiredToResolve = 2;

    public StoreActionPlanService(
        AppDbContext db,
        IStoreAccessService storeAccess,
        IActionPlanRoleService actionPlanRoles,
        IActionPlanSeverityConfigService severityConfig,
        IStoreService stores,
        IDashboardService dashboard,
        INinetyDayTurnoverService ninetyDay,
        IRetentionService retention,
        IEarlyWarningService earlyWarning,
        IExitInterviewService exitInterview,
        IRecommendationTemplateService templates)
    {
        _db = db;
        _storeAccess = storeAccess;
        _actionPlanRoles = actionPlanRoles;
        _severityConfig = severityConfig;
        _stores = stores;
        _dashboard = dashboard;
        _ninetyDay = ninetyDay;
        _retention = retention;
        _earlyWarning = earlyWarning;
        _exitInterview = exitInterview;
        _templates = templates;
    }

    // ────────────────────────────── Read APIs ──────────────────────────────

    public async Task<StoreActionPlanDto?> GetForStoreAsync(string storeName, string role, string? email)
    {
        if (!await _storeAccess.CanAccessStoreAsync(role, email, storeName)) return null;

        var plan = await _db.StoreActionPlans
            .Where(p => p.StoreName == storeName)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        var responsible = await _actionPlanRoles.GetEffectiveResponsiblePartyAsync(storeName);
        // CanAccessStoreAsync above already confirmed this role/email pair
        // matches the store's own email column for that role, so it's safe
        // to grant notes access purely on "is this the delegated role."
        var canAddNotes = responsible != null && role == responsible.Role;

        if (plan == null)
        {
            return new StoreActionPlanDto
            {
                StoreName = storeName,
                Status = "None",
                ResponsibleParty = responsible,
                CanAddNotes = canAddNotes,
            };
        }

        var recommendations = await _db.ActionPlanRecommendations
            .Where(r => r.StoreActionPlanId == plan.Id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
        var recTemplates = await _templates.GetAllAsync();
        var isArabic = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var notes = await _db.ActionPlanNotes
            .Where(n => n.StoreActionPlanId == plan.Id)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();

        return new StoreActionPlanDto
        {
            Id = plan.Id,
            StoreName = plan.StoreName,
            Status = plan.Status,
            CreatedAt = plan.CreatedAt,
            CreatedMonth = plan.CreatedMonth,
            CreatedYear = plan.CreatedYear,
            ResolvedAt = plan.ResolvedAt,
            ResolvedReason = plan.ResolvedReason,
            BaselineTurnoverRate = plan.BaselineTurnoverRate,
            BaselineEarlyLeaverRate = plan.BaselineEarlyLeaverRate,
            BaselineRetentionRate = plan.BaselineRetentionRate,
            DetectedIssuesSummary = EvidenceTranslator.Translate(plan.DetectedIssuesSummary, isArabic),
            HealthyStreakCount = plan.HealthyStreakCount,
            ResponsibleParty = responsible,
            CanAddNotes = canAddNotes,
            Recommendations = recommendations.Select(r => new ActionPlanRecommendationDto
            {
                Id = r.Id,
                SignalCode = r.SignalCode,
                Category = r.Category,
                RecommendationText = RecommendationTemplateService.Resolve(recTemplates, r.SignalCode, r.Category, r.RecommendationText, isArabic),
                CreatedAt = r.CreatedAt,
                IsCompleted = r.IsCompleted,
                CompletedAt = r.CompletedAt,
                CompletedByName = r.CompletedByName,
            }).ToList(),
            Notes = notes.Select(n => new ActionPlanNoteDto
            {
                AuthorName = n.AuthorName,
                AuthorRole = n.AuthorRole,
                NoteText = n.NoteText,
                CreatedAt = n.CreatedAt,
            }).ToList(),
        };
    }

    // ────────────────────────────── Notes (write) ──────────────────────────────

    public async Task<(bool success, string message, ActionPlanNoteDto? note)> AddNoteAsync(
        string storeName, string role, string? email, int authorUserId, string authorName, string noteText)
    {
        if (string.IsNullOrWhiteSpace(noteText)) return (false, "Note text is required.", null);

        var effectiveRole = await _actionPlanRoles.GetEffectiveRoleAsync(storeName);
        if (effectiveRole == null || role != effectiveRole) return (false, "Not permitted to add notes.", null);
        // Write permission ignores the access-area widening — even if the Action
        // Center view is opened company-wide, a restricted role may only add notes
        // to stores it actually owns.
        if (!await _storeAccess.CanManageStoreAsync(role, email, storeName)) return (false, "Not permitted to add notes.", null);

        var plan = await _db.StoreActionPlans
            .Where(p => p.StoreName == storeName)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        if (plan == null) return (false, "No action plan exists for this store yet.", null);

        var note = new ActionPlanNote
        {
            StoreActionPlanId = plan.Id,
            AuthorUserId = authorUserId,
            AuthorName = authorName,
            AuthorRole = role,
            NoteText = noteText.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.ActionPlanNotes.Add(note);
        await _db.SaveChangesAsync();

        return (true, "Note added.", new ActionPlanNoteDto
        {
            AuthorName = note.AuthorName,
            AuthorRole = note.AuthorRole,
            NoteText = note.NoteText,
            CreatedAt = note.CreatedAt,
        });
    }

    // ────────────────────────────── Detection ──────────────────────────────

    public async Task RunDetectionForPeriodAsync(int month, int year)
    {
        var storeNames = await _db.StoreReferences
            .Where(s => s.Month == month && s.Year == year)
            .Select(s => s.StoreName)
            .Distinct()
            .ToListAsync();

        foreach (var storeName in storeNames)
        {
            await EvaluateStoreAsync(storeName, month, year);
        }
    }

    private async Task EvaluateStoreAsync(string storeName, int month, int year)
    {
        var existing = await _db.StoreActionPlans
            .FirstOrDefaultAsync(p => p.StoreName == storeName && p.Status == "Active");

        // Already evaluated this exact period for this plan — re-running detection
        // (e.g. after a corrective single-file re-upload) must not double-count the
        // monthly cycle toward auto-resolution or re-add the same signals.
        if (existing != null && existing.LastEvaluatedMonth == month && existing.LastEvaluatedYear == year)
        {
            return;
        }

        var metrics = await ComputeSignalsAsync(storeName, month, year);
        await LogSignalOccurrencesAsync(storeName, month, year, metrics.Signals);

        if (existing == null)
        {
            if (metrics.Signals.Count == 0) return;

            var bands = await _severityConfig.GetAsync();
            var distinctNewSignals = metrics.Signals.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var creationSeverity = ComputeSeverity("Active", distinctNewSignals, bands);
            var createdAt = DateTime.UtcNow;

            var plan = new StoreActionPlan
            {
                StoreName = storeName,
                Status = "Active",
                CreatedAt = createdAt,
                CreatedMonth = month,
                CreatedYear = year,
                BaselineTurnoverRate = metrics.TurnoverRate,
                BaselineEarlyLeaverRate = metrics.EarlyLeaverRate,
                BaselineRetentionRate = metrics.RetentionRate,
                DetectedIssuesSummary = string.Join("\n", metrics.Signals.Select(s => s.Evidence)),
                HealthyStreakCount = 0,
                LastEvaluatedMonth = month,
                LastEvaluatedYear = year,
                // Critical plans get a shorter, urgent window; every other plan
                // gets a full 90-day working window regardless of when in the
                // quarter it qualifies. Admin can still override via SetAssignmentAsync.
                TargetResolutionDate = DateOnly.FromDateTime(createdAt.AddDays(creationSeverity == "Critical" ? CriticalTargetDays : StandardTargetDays)),
            };
            _db.StoreActionPlans.Add(plan);
            await _db.SaveChangesAsync();

            AddRecommendations(plan.Id, metrics.Signals);
            RecordMetricSnapshot(plan.Id, month, year, metrics);
            await _db.SaveChangesAsync();
            return;
        }

        var existingCodes = (await _db.ActionPlanRecommendations
            .Where(r => r.StoreActionPlanId == existing.Id)
            .Select(r => r.SignalCode)
            .Distinct()
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // New problems surfacing while a plan is already active are folded into
        // it — only one active plan per store, so this is how "multiple detected
        // problems grouped into the same plan" is satisfied over time, not just
        // at creation.
        var newSignals = metrics.Signals.Where(s => !existingCodes.Contains(s.Code)).ToList();
        if (newSignals.Count > 0)
        {
            AddRecommendations(existing.Id, newSignals);
            var newEvidence = string.Join("\n", newSignals.Select(s => s.Evidence));
            existing.DetectedIssuesSummary = string.IsNullOrEmpty(existing.DetectedIssuesSummary)
                ? newEvidence
                : existing.DetectedIssuesSummary + "\n" + newEvidence;
        }

        if (metrics.Signals.Count == 0)
        {
            existing.HealthyStreakCount += 1;
            if (existing.HealthyStreakCount >= HealthyCyclesRequiredToResolve)
            {
                existing.Status = "Resolved";
                existing.ResolvedAt = DateTime.UtcNow;
                existing.ResolvedReason = "Auto_Improvement";
            }
        }
        else
        {
            existing.HealthyStreakCount = 0;
        }

        existing.LastEvaluatedMonth = month;
        existing.LastEvaluatedYear = year;

        RecordMetricSnapshot(existing.Id, month, year, metrics);

        await _db.SaveChangesAsync();
    }

    /// <summary>One row per detection cycle for a store with a plan — captured
    /// regardless of whether any signal fired that cycle, so the Action Center
    /// can chart real progress (baseline vs. now) instead of a single frozen
    /// creation-time snapshot. Purely additive: the legacy detection logic above
    /// and the old Store Action Plan page never read this table.</summary>
    private void RecordMetricSnapshot(int planId, int month, int year, PeriodMetrics metrics)
    {
        _db.ActionPlanMetricSnapshots.Add(new ActionPlanMetricSnapshot
        {
            StoreActionPlanId = planId,
            Month = month,
            Year = year,
            TurnoverRate = metrics.TurnoverRate,
            EarlyLeaverRate = metrics.EarlyLeaverRate,
            RetentionRate = metrics.RetentionRate,
            SignalCount = metrics.Signals.Count,
            RecordedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Logs one signal_occurrences row per newly-fired signal for this
    /// store/period, independent of StoreActionPlan/ActionPlanRecommendation —
    /// this is what the persistence rule (2 of the last 3 data-available
    /// evaluation periods) reads, so it must exist even before any plan is
    /// created and must survive plan resolution. Idempotent: safe to call again
    /// for a period already logged (e.g. a corrective re-upload before a plan
    /// exists yet, when EvaluateStoreAsync has no early-exit guard to rely on).</summary>
    private async Task LogSignalOccurrencesAsync(string storeName, int month, int year, List<FiredSignal> signals)
    {
        if (signals.Count == 0) return;

        var alreadyLogged = (await _db.SignalOccurrences
            .Where(o => o.StoreName == storeName && o.Month == month && o.Year == year)
            .Select(o => o.SignalCode)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var signal in signals)
        {
            if (alreadyLogged.Contains(signal.Code)) continue;
            _db.SignalOccurrences.Add(new SignalOccurrence
            {
                StoreName = storeName,
                SignalCode = signal.Code,
                Month = month,
                Year = year,
                OccurredAt = DateTime.UtcNow,
                IsBackfilled = false,
            });
        }
    }

    /// <summary>Whether a signal is "persistent" for a store: it occurred in at
    /// least 2 of the last 3 *data-available evaluation periods* — periods
    /// where a StoreReference row exists for that store, i.e. a period was
    /// actually uploaded and evaluated. A period with no upload is skipped
    /// entirely rather than counted as healthy, so a store cannot dodge
    /// persistence by having a gap in uploads, and cannot be falsely flagged
    /// persistent from periods that were never evaluated.</summary>
    public async Task<bool> IsSignalPersistentAsync(string storeName, string signalCode, int asOfMonth, int asOfYear)
    {
        var evaluationPeriods = await _db.StoreReferences
            .Where(s => s.StoreName == storeName && (s.Year * 12 + s.Month) <= (asOfYear * 12 + asOfMonth))
            .Select(s => new { s.Month, s.Year })
            .Distinct()
            .ToListAsync();

        var last3 = evaluationPeriods
            .OrderByDescending(p => p.Year * 12 + p.Month)
            .Take(3)
            .ToList();
        if (last3.Count < 2) return false;

        var occurrences = await _db.SignalOccurrences
            .Where(o => o.StoreName == storeName && o.SignalCode == signalCode)
            .Select(o => new { o.Month, o.Year })
            .ToListAsync();

        var occurrenceCount = occurrences.Count(o => last3.Any(p => p.Month == o.Month && p.Year == o.Year));
        return occurrenceCount >= 2;
    }

    /// <summary>Read-only view of a store's signal_occurrences log for the
    /// Action Center detail page — does not touch detection, severity, or
    /// persistence logic, it only presents what's already there. Periods
    /// between the store's first and last StoreReference upload that have no
    /// upload of their own are included with HasData = false and an empty
    /// signal list, so the UI can show them as "no data" rather than silently
    /// implying they were clean (matching the same rule IsSignalPersistentAsync
    /// already applies). Returns null if the role/email can't access the store,
    /// same quiet-filtering convention as GetForStoreAsync.</summary>
    public async Task<SignalHistoryDto?> GetSignalHistoryAsync(string storeName, string role, string? email)
    {
        if (!await _storeAccess.CanAccessStoreAsync(role, email, storeName)) return null;

        var availablePeriods = (await _db.StoreReferences
            .Where(s => s.StoreName == storeName)
            .Select(s => new { s.Month, s.Year })
            .Distinct()
            .ToListAsync())
            .Select(p => p.Year * 12 + p.Month)
            .ToHashSet();

        var result = new SignalHistoryDto();
        if (availablePeriods.Count == 0) return result;

        var occurrences = await _db.SignalOccurrences
            .Where(o => o.StoreName == storeName)
            .ToListAsync();
        var occurrencesByPeriod = occurrences
            .GroupBy(o => o.Year * 12 + o.Month)
            .ToDictionary(g => g.Key, g => g.Select(o => o.SignalCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var minKey = availablePeriods.Min();
        var maxKey = availablePeriods.Max();
        for (var key = maxKey; key >= minKey; key--)
        {
            var year = (key - 1) / 12;
            var month = key - year * 12;
            var hasData = availablePeriods.Contains(key);
            result.Periods.Add(new SignalHistoryPeriodDto
            {
                Month = month,
                Year = year,
                Label = new DateOnly(year, month, 1).ToString("MMM yy"),
                HasData = hasData,
                Signals = hasData && occurrencesByPeriod.TryGetValue(key, out var codes) ? codes : new List<string>(),
            });
        }

        var maxYear = (maxKey - 1) / 12;
        var maxMonth = maxKey - maxYear * 12;

        var distinctSignalCodes = occurrences.Select(o => o.SignalCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var code in distinctSignalCodes)
        {
            result.Persistence.Add(new SignalPersistenceDto
            {
                SignalCode = code,
                IsPersistent = await IsSignalPersistentAsync(storeName, code, maxMonth, maxYear),
            });
        }

        return result;
    }

    /// <summary>Monthly Overall Turnover % / 90-Day Turnover % history for the
    /// detail page's Monthly Turnover Performance table. Reads the same
    /// DashboardService/NinetyDayTurnoverService KPI calls ComputeSignalsAsync
    /// already makes for live detection, one period at a time, for every
    /// period this store has StoreReference data — no new calculation rule,
    /// no new data source. Returns null if the role/email can't access the
    /// store, same quiet-filtering convention as GetForStoreAsync.</summary>
    public async Task<MonthlyTurnoverHistoryDto?> GetMonthlyTurnoverHistoryAsync(string storeName, string role, string? email)
    {
        if (!await _storeAccess.CanAccessStoreAsync(role, email, storeName)) return null;

        var periods = (await _db.StoreReferences
            .Where(s => s.StoreName == storeName)
            .Select(s => new { s.Month, s.Year })
            .Distinct()
            .ToListAsync())
            .OrderByDescending(p => p.Year * 12 + p.Month)
            .ToList();

        var result = new MonthlyTurnoverHistoryDto();
        foreach (var p in periods)
        {
            var kpi = await _dashboard.GetKpisAsync(p.Month, p.Year, storeName, SystemRole, null);
            var ninety = await _ninetyDay.GetKpiAsync(p.Month, p.Year, storeName, SystemRole, null);

            result.Periods.Add(new MonthlyTurnoverPeriodDto
            {
                Month = p.Month,
                Year = p.Year,
                Label = new DateOnly(p.Year, p.Month, 1).ToString("MMM yy"),
                OverallTurnoverRate = kpi.TotalHeadcount > 0 ? kpi.TurnoverRate : null,
                NinetyDayTurnoverRate = ninety.TotalHires > 0 ? ninety.Rate : null,
            });
        }

        return result;
    }

    /// <summary>Replays historical signal detection directly (bypassing
    /// EvaluateStoreAsync and its "already evaluated this period" early-exit
    /// guard, which would otherwise silently skip logging occurrences for any
    /// store/period already tied to an existing plan) against every past
    /// StoreReference period, for the 6 signals whose source data is retained
    /// per period. EARLY_WARNING_WATCHLIST is excluded — EarlyWarningService
    /// only reflects current active-employee risk state, so it cannot be
    /// reconstructed for a past period; it starts logging live from go-live.
    /// Idempotent — re-running skips any store/period/signal already logged.</summary>
    public async Task<int> RunHistoricalSignalBackfillAsync()
    {
        var periods = await _db.StoreReferences
            .Select(s => new { s.StoreName, s.Month, s.Year })
            .Distinct()
            .ToListAsync();

        var written = 0;
        foreach (var period in periods)
        {
            var metrics = await ComputeSignalsAsync(period.StoreName, period.Month, period.Year);
            var replayable = metrics.Signals.Where(s => s.Code != "EARLY_WARNING_WATCHLIST").ToList();
            if (replayable.Count == 0) continue;

            var alreadyLogged = (await _db.SignalOccurrences
                .Where(o => o.StoreName == period.StoreName && o.Month == period.Month && o.Year == period.Year)
                .Select(o => o.SignalCode)
                .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var signal in replayable)
            {
                if (alreadyLogged.Contains(signal.Code)) continue;
                _db.SignalOccurrences.Add(new SignalOccurrence
                {
                    StoreName = period.StoreName,
                    SignalCode = signal.Code,
                    Month = period.Month,
                    Year = period.Year,
                    OccurredAt = DateTime.UtcNow,
                    IsBackfilled = true,
                });
                written++;
            }
            await _db.SaveChangesAsync();
        }
        return written;
    }

    private void AddRecommendations(int planId, List<FiredSignal> signals)
    {
        foreach (var signal in signals)
        {
            foreach (var text in signal.RecommendationTexts)
            {
                _db.ActionPlanRecommendations.Add(new ActionPlanRecommendation
                {
                    StoreActionPlanId = planId,
                    SignalCode = signal.Code,
                    Category = signal.Category,
                    RecommendationText = text,
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }
    }

    // ────────────────────────────── Signal evaluation ──────────────────────────────

    private class FiredSignal
    {
        public string Code { get; set; } = "";
        public string Category { get; set; } = "";
        public string Evidence { get; set; } = "";
        public List<string> RecommendationTexts { get; set; } = new();
    }

    private class PeriodMetrics
    {
        public double? TurnoverRate;
        public double? EarlyLeaverRate;
        public double? RetentionRate;
        public List<FiredSignal> Signals { get; } = new();
    }

    /// <summary>Translates the plain-English "Detected Issues" sentences
    /// (StoreActionPlan.DetectedIssuesSummary — one per fired signal, newline-
    /// joined, accumulated over time) to Arabic for display, without touching
    /// what's stored. Each sentence always has one of the 8 fixed shapes below
    /// (produced only by the Evidence interpolations in ComputeSignalsAsync),
    /// so a sentence is translated by extracting its numbers with a regex that
    /// mirrors that exact shape and re-formatting them into the Arabic
    /// equivalent — same live numbers, translated wording. A line that matches
    /// none of the 8 patterns (unexpected/legacy data) is left as-is rather
    /// than risk mistranslating it.</summary>
    private static class EvidenceTranslator
    {
        private static readonly (Regex Pattern, Func<Match, string> ToArabic)[] Rules =
        {
            (new Regex(@"^Overall turnover rate (?<rate>[\d.]+)% \(headcount (?<hc>\d+)\) is at or above the (?<th>\d+)% threshold\.$"),
                m => $"معدل الدوران الإجمالي {m.Groups["rate"].Value}% (عدد الموظفين {m.Groups["hc"].Value}) يساوي أو يتجاوز حد الـ {m.Groups["th"].Value}%."),

            (new Regex(@"^Early turnover \(within 90 days\) is (?<rate>[\d.]+)% \((?<early>\d+) of (?<total>\d+) hires\)\.$"),
                m => $"الترك المبكر (خلال 90 يوم) {m.Groups["rate"].Value}% ({m.Groups["early"].Value} من أصل {m.Groups["total"].Value} توظيف)."),

            (new Regex(@"^6-month retention is (?<rate>[\d.]+)% \((?<retained>\d+) of (?<total>\d+) hires\), below the (?<th>\d+)% threshold\.$"),
                m => $"معدل الاحتفاظ بعد 6 أشهر {m.Groups["rate"].Value}% ({m.Groups["retained"].Value} من أصل {m.Groups["total"].Value} توظيف)، أقل من حد الـ {m.Groups["th"].Value}%."),

            (new Regex(@"^(?<n>\d+) different Store Leaders recorded in the last (?<m>\d+) months\.$"),
                m => $"تسجيل {m.Groups["n"].Value} قادة فروع مختلفين خلال آخر {m.Groups["m"].Value} أشهر."),

            (new Regex(@"^Positive exit sentiment is (?<pct>[\d.]+)% across (?<n>\d+) exit interviews\.$"),
                m => $"نسبة المشاعر الإيجابية عند الخروج {m.Groups["pct"].Value}% من أصل {m.Groups["n"].Value} مقابلة خروج."),

            // The quoted label is already Arabic verbatim from the exit-interview
            // form (see MapReasonToRecommendations) — only the English scaffold
            // around it is translated.
            // The trailing percent may or may not have a space before "%" depending
            // on which culture was active when P0 formatted it — tolerate either.
            (new Regex("^\"(?<label>.+)\" is cited in (?<value>\\d+) of (?<total>\\d+) exit interviews \\((?<pct>\\d+)\\s?%\\)\\.$"),
                m => $"\"{m.Groups["label"].Value}\" هو السبب الأكثر تكرارًا في {m.Groups["value"].Value} من أصل {m.Groups["total"].Value} مقابلة خروج ({m.Groups["pct"].Value}%)."),

            (new Regex(@"^(?<n>\d+) currently active employees are flagged high-risk on the early-warning watchlist\.$"),
                m => $"{m.Groups["n"].Value} موظف نشط حاليًا مصنّف كمخاطرة عالية في قائمة الإنذار المبكر."),

            (new Regex(@"^Turnover is elevated but no specific driver stands out from the available data\.$"),
                _ => "معدل الدوران مرتفع لكن لا يوجد سبب محدد بارز من البيانات المتاحة."),
        };

        public static string Translate(string englishSummary, bool arabic)
        {
            if (!arabic || string.IsNullOrEmpty(englishSummary)) return englishSummary;

            var lines = englishSummary.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var (pattern, toArabic) in Rules)
                {
                    var match = pattern.Match(lines[i]);
                    if (match.Success) { lines[i] = toArabic(match); break; }
                }
            }
            return string.Join("\n", lines);
        }
    }

    private async Task<PeriodMetrics> ComputeSignalsAsync(string storeName, int month, int year)
    {
        var metrics = new PeriodMetrics();

        // Overall turnover rate.
        var kpi = await _dashboard.GetKpisAsync(month, year, storeName, SystemRole, null);
        if (kpi.TotalHeadcount > 0)
        {
            metrics.TurnoverRate = kpi.TurnoverRate;
            if (kpi.TotalHeadcount >= MinHeadcountForRateSignals && kpi.TurnoverRate >= OverallTurnoverRateThreshold)
            {
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "HIGH_OVERALL_TURNOVER",
                    Category = "Retention",
                    Evidence = $"Overall turnover rate {kpi.TurnoverRate:F1}% (headcount {kpi.TotalHeadcount}) is at or above the {OverallTurnoverRateThreshold:F0}% threshold.",
                    RecommendationTexts =
                    {
                        "Review overall staffing stability and scheduling fairness.",
                        "Conduct stay-interviews with current team members.",
                        "Assess workload distribution relative to store volume.",
                    },
                });
            }
        }

        // Early turnover within first 90 days.
        var ninety = await _ninetyDay.GetKpiAsync(month, year, storeName, SystemRole, null);
        if (ninety.TotalHires > 0)
        {
            metrics.EarlyLeaverRate = ninety.Rate;
            if (!ninety.IsProvisional && ninety.TotalHires >= MinHiresForEarlyLeaverSignal && ninety.Rate >= EarlyLeaverRateThreshold)
            {
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "EARLY_TURNOVER_90D",
                    Category = "Onboarding",
                    Evidence = $"Early turnover (within 90 days) is {ninety.Rate:F1}% ({ninety.EarlyLeavers} of {ninety.TotalHires} hires).",
                    RecommendationTexts =
                    {
                        "Improve onboarding process for new hires.",
                        "Assign a mentor/buddy and follow up with new hires during their first 90 days.",
                        "Review Store Leader coaching on new-hire integration.",
                    },
                });
            }
        }

        // Retention at the 6-month mark.
        var milestones = await _retention.GetMilestonesAsync(storeName, SystemRole, null);
        var sixMonth = milestones.FirstOrDefault(m => m.Label == "6 Months");
        if (sixMonth != null && sixMonth.TotalHires > 0)
        {
            metrics.RetentionRate = sixMonth.RetentionRate;
            if (sixMonth.TotalHires >= MinHiresForRetentionSignal && sixMonth.RetentionRate < SixMonthRetentionThreshold)
            {
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "LOW_RETENTION_6M",
                    Category = "Retention",
                    Evidence = $"6-month retention is {sixMonth.RetentionRate:F1}% ({sixMonth.Retained} of {sixMonth.TotalHires} hires), below the {SixMonthRetentionThreshold:F0}% threshold.",
                    RecommendationTexts =
                    {
                        "Review engagement for employees in the 3-6 month tenure range.",
                        "Check for consistent scheduling and growth opportunities before the 1-year mark.",
                    },
                });
            }
        }

        // Store Leader stability over the trailing window.
        var currentKey = year * 12 + month;
        var cutoffKey = currentKey - (LeadershipChangeLookbackMonths - 1);
        var recentRefs = await _db.StoreReferences
            .Where(s => s.StoreName == storeName && (s.Year * 12 + s.Month) >= cutoffKey && (s.Year * 12 + s.Month) <= currentKey)
            .Select(s => s.StoreLeader)
            .ToListAsync();
        var distinctLeaders = recentRefs.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctLeaders >= LeadershipChangeCountThreshold)
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "LEADERSHIP_INSTABILITY",
                Category = "Leadership",
                Evidence = $"{distinctLeaders} different Store Leaders recorded in the last {LeadershipChangeLookbackMonths} months.",
                RecommendationTexts =
                {
                    "Review Store Leader coaching and support.",
                    "Ensure operational continuity during leadership transitions.",
                    "Assess whether additional Head Manager oversight is needed.",
                },
            });
        }

        // Exit interview sentiment and dominant resignation reason.
        var filter = new ExitInterviewFilter { Store = storeName };

        var sentiment = await _exitInterview.GetSentimentSummaryAsync(filter, SystemRole, null);
        if (sentiment.TotalResponses >= MinExitResponsesForSentimentSignal && sentiment.PositivePercent < ExitSentimentPositiveThreshold)
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "LOW_EXIT_SENTIMENT",
                Category = "Culture",
                Evidence = $"Positive exit sentiment is {sentiment.PositivePercent:F1}% across {sentiment.TotalResponses} exit interviews.",
                RecommendationTexts =
                {
                    "Conduct stay-interviews with current team to catch issues early.",
                    "Review recent management/culture incidents reported at exit.",
                },
            });
        }

        var reasons = await _exitInterview.GetReasonsForLeavingAsync(filter, SystemRole, null);
        var totalReasonResponses = reasons.Sum(r => r.Value);
        if (totalReasonResponses >= MinExitResponsesForReasonSignal)
        {
            var top = reasons.OrderByDescending(r => r.Value).FirstOrDefault();
            if (top != null && top.Value / (double)totalReasonResponses >= ReasonConcentrationShareThreshold)
            {
                var (category, recs) = MapReasonToRecommendations(top.Label);
                metrics.Signals.Add(new FiredSignal
                {
                    Code = "REASON_CONCENTRATION",
                    Category = category,
                    Evidence = $"\"{top.Label}\" is cited in {top.Value} of {totalReasonResponses} exit interviews ({top.Value / (double)totalReasonResponses:P0}).",
                    RecommendationTexts = recs,
                });
            }
        }

        // Early-warning watchlist (currently active, at-risk employees).
        var watchlist = await _earlyWarning.GetSummaryAsync(storeName, SystemRole, null);
        if (watchlist.HighRiskCount >= EarlyWarningHighRiskThreshold)
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "EARLY_WARNING_WATCHLIST",
                Category = "Monitoring",
                Evidence = $"{watchlist.HighRiskCount} currently active employees are flagged high-risk on the early-warning watchlist.",
                RecommendationTexts =
                {
                    "Proactively check in with employees flagged as at-risk.",
                    "Review the early-warning watchlist with the Store Leader.",
                },
            });
        }

        // Honest fallback: turnover is elevated but nothing more specific explains it.
        if (metrics.Signals.Any(s => s.Code == "HIGH_OVERALL_TURNOVER") &&
            !metrics.Signals.Any(s => s.Code is "EARLY_TURNOVER_90D" or "LEADERSHIP_INSTABILITY" or "REASON_CONCENTRATION" or "LOW_EXIT_SENTIMENT"))
        {
            metrics.Signals.Add(new FiredSignal
            {
                Code = "NO_DOMINANT_DRIVER",
                Category = "General",
                Evidence = "Turnover is elevated but no specific driver stands out from the available data.",
                RecommendationTexts =
                {
                    "No single cause stands out — recommend a manual review of recent exits with the Store Leader.",
                },
            });
        }

        return metrics;
    }

    /// <summary>
    /// ReasonForLeaving is stored verbatim from the Arabic Microsoft Forms export
    /// (see UploadService.Get(row, "برجاء اختيار سبب ترك العمل")) — never English —
    /// so category matching must be done on Arabic keywords, not English ones.
    /// The exact fixed option list used by the Form is external to this repo, so
    /// these are conservative, common Arabic terms for each category (root forms
    /// like "معامل" deliberately match "معاملة"/"المعاملة"). Expand this list once
    /// the authoritative Form option list is confirmed with the business.
    /// </summary>
    private static (string Category, List<string> Recommendations) MapReasonToRecommendations(string label)
    {
        var l = label ?? "";

        if (l.Contains("راتب") || l.Contains("مرتب") || l.Contains("أجر") || l.Contains("اجر") || l.Contains("بدل"))
            return ("Compensation", new List<string>
            {
                "Review pay competitiveness against the local market.",
                "Check for pay-equity issues within the store.",
            });

        if (l.Contains("ضغط") || l.Contains("ساعات") || l.Contains("جدول") || l.Contains("دوام") || l.Contains("وردي"))
            return ("Workload", new List<string>
            {
                "Review staffing levels against sales volume.",
                "Audit shift scheduling fairness.",
            });

        if (l.Contains("معامل") || l.Contains("إدارة") || l.Contains("اداره") || l.Contains("احترام") || l.Contains("عادل") || l.Contains("عدل"))
            return ("Management", new List<string>
            {
                "Coach the Store Leader on people management.",
                "Review recent complaints or incidents.",
            });

        if (l.Contains("تدريب") || l.Contains("تأهيل") || l.Contains("تاهيل"))
            return ("Onboarding", new List<string>
            {
                "Improve onboarding process for new hires.",
                "Review Store Leader coaching on new-hire integration.",
            });

        return ("Other", new List<string>
        {
            "Review the dominant resignation reason with the Store Leader and plan a targeted response.",
        });
    }

    // ════════════════════════════ Action Center ════════════════════════════
    // Everything below reads/writes the same store_action_plans /
    // action_plan_recommendations rows the legacy engine above already
    // maintains, plus the new (purely additive) columns/table — so the old
    // Store Action Plan page keeps working completely unchanged.

    private const int StalledAgeDaysThreshold = 45;
    private const double TrendFlatMarginPoints = 1.0;

    // Target Resolution Date is set once at plan creation from CreatedAt, not
    // from the calendar quarter's end — a plan qualifying near quarter-end
    // still gets a full working window instead of just a few days. Calendar
    // quarter remains purely a reporting bucket (see GetReportingQuarter),
    // computed independently of this date.
    private const int StandardTargetDays = 90;
    private const int CriticalTargetDays = 30;

    /// <summary>Purely a reporting bucket for grouping/rollups — derived from
    /// CreatedAt, independent of TargetResolutionDate. Not stored.</summary>
    private static (int Quarter, int Year) GetReportingQuarter(DateTime createdAt) =>
        ((createdAt.Month - 1) / 3 + 1, createdAt.Year);

    /// <summary>Labels which window TargetResolutionDate reflects, purely by
    /// comparing it against CreatedAt — nothing is stored to say "this plan
    /// opened Critical." A small tolerance absorbs the DateOnly truncation of
    /// CreatedAt's time-of-day; anything outside both windows means an Admin
    /// moved the date manually via SetAssignmentAsync.</summary>
    private static (string? Type, int? Days) ComputeTargetWindow(DateTime createdAt, DateOnly? targetDate)
    {
        if (targetDate == null) return (null, null);
        var days = targetDate.Value.DayNumber - DateOnly.FromDateTime(createdAt).DayNumber;
        if (days is >= CriticalTargetDays - 2 and <= CriticalTargetDays + 2) return ("Critical", days);
        if (days is >= StandardTargetDays - 2 and <= StandardTargetDays + 2) return ("Standard", days);
        return ("Custom", days);
    }

    /// <summary>Severity is computed live from whatever ActionPlanSeverityBandConfig
    /// is current, never stored — so changing the cutoffs on the Settings page
    /// immediately re-classifies every open and historical plan's displayed
    /// severity without touching any row in the database.</summary>
    private static string ComputeSeverity(string status, int distinctSignalCount, ActionPlanSeverityBandConfig bands)
    {
        if (status != "Active") return "None";
        if (distinctSignalCount >= bands.CriticalMinSignals) return "Critical";
        if (distinctSignalCount >= bands.HighMinSignals) return "High";
        if (distinctSignalCount >= bands.MediumMinSignals) return "Medium";
        return "Low";
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0,
    };

    /// <summary>Improving/Worsening based on the change in Turnover Rate between
    /// the plan's first and most recent metric snapshot — the one metric present
    /// on every snapshot. "New" until there are at least 2 snapshots to compare.</summary>
    private static string ComputeTrend(List<ActionPlanMetricSnapshot> snapshots)
    {
        var withTurnover = snapshots.Where(s => s.TurnoverRate.HasValue).OrderBy(s => s.Year).ThenBy(s => s.Month).ToList();
        if (withTurnover.Count < 2) return "New";
        var delta = withTurnover.Last().TurnoverRate!.Value - withTurnover.First().TurnoverRate!.Value;
        if (delta <= -TrendFlatMarginPoints) return "Improving";
        if (delta >= TrendFlatMarginPoints) return "Worsening";
        return "Flat";
    }

    private async Task<Dictionary<string, string>> GetOperationManagerByStoreAsync(List<string> storeNames)
    {
        if (storeNames.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var refs = await _db.StoreReferences.Where(s => storeNames.Contains(s.StoreName)).ToListAsync();
        return refs.GroupBy(s => s.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First().OperationManager ?? "",
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<ActionCenterStoreRowDto>> GetActionCenterStoresAsync(string role, string? email)
    {
        var storeRefs = await _stores.GetStoresAsync(null, null, role, email);
        var storeNames = storeRefs
            .Select(s => s.StoreName?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (storeNames.Count == 0) return new List<ActionCenterStoreRowDto>();

        var responsibleByStore = await _actionPlanRoles.GetEffectiveResponsiblePartiesAsync(storeNames);
        var bands = await _severityConfig.GetAsync();

        var allPlans = await _db.StoreActionPlans.Where(p => storeNames.Contains(p.StoreName)).ToListAsync();
        var plansByStore = allPlans.GroupBy(p => p.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var latestPlanIds = plansByStore.Values.Select(list => list.OrderByDescending(p => p.CreatedAt).First().Id).ToList();
        var recsByPlan = (await _db.ActionPlanRecommendations.Where(r => latestPlanIds.Contains(r.StoreActionPlanId)).ToListAsync())
            .GroupBy(r => r.StoreActionPlanId).ToDictionary(g => g.Key, g => g.ToList());
        var snapshotsByPlan = (await _db.ActionPlanMetricSnapshots.Where(s => latestPlanIds.Contains(s.StoreActionPlanId)).ToListAsync())
            .GroupBy(s => s.StoreActionPlanId).ToDictionary(g => g.Key, g => g.ToList());

        var asOf = DateTime.UtcNow;
        var result = new List<ActionCenterStoreRowDto>();
        foreach (var storeName in storeNames)
        {
            plansByStore.TryGetValue(storeName, out var storePlans);
            var latest = storePlans?.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            responsibleByStore.TryGetValue(storeName, out var responsible);

            if (latest == null)
            {
                result.Add(new ActionCenterStoreRowDto
                {
                    StoreName = storeName,
                    PlanStatus = "None",
                    ResponsibleName = responsible?.Name,
                    ResponsibleRole = responsible?.Role,
                });
                continue;
            }

            var recs = recsByPlan.TryGetValue(latest.Id, out var r) ? r : new List<ActionPlanRecommendation>();
            var distinctSignals = recs.Select(x => x.SignalCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var snapshots = snapshotsByPlan.TryGetValue(latest.Id, out var snaps) ? snaps : new List<ActionPlanMetricSnapshot>();
            var ageDays = (int)((latest.Status == "Resolved" && latest.ResolvedAt.HasValue ? latest.ResolvedAt.Value : asOf) - latest.CreatedAt).TotalDays;
            var tasksCompleted = recs.Count(x => x.IsCompleted);

            result.Add(new ActionCenterStoreRowDto
            {
                StoreName = storeName,
                PlanStatus = latest.Status,
                PlanId = latest.Id,
                Severity = ComputeSeverity(latest.Status, distinctSignals, bands),
                SignalCount = distinctSignals,
                AgeDays = ageDays,
                IsChronic = (storePlans?.Count ?? 0) > 1,
                IsStalled = latest.Status == "Active" && ageDays >= StalledAgeDaysThreshold && tasksCompleted == 0,
                Trend = ComputeTrend(snapshots),
                ResponsibleName = responsible?.Name,
                ResponsibleRole = responsible?.Role,
                AssignedToName = latest.AssignedToName,
                TargetResolutionDate = latest.TargetResolutionDate,
                TasksTotal = recs.Count,
                TasksCompleted = tasksCompleted,
                ReportingQuarter = GetReportingQuarter(latest.CreatedAt).Quarter,
                ReportingYear = GetReportingQuarter(latest.CreatedAt).Year,
            });
        }

        return result.OrderByDescending(r => SeverityRank(r.Severity)).ThenByDescending(r => r.AgeDays).ToList();
    }

    public async Task<ActionCenterSummaryDto> GetActionCenterSummaryAsync(string role, string? email)
    {
        var summary = new ActionCenterSummaryDto();
        var storeRefs = await _stores.GetStoresAsync(null, null, role, email);
        var storeNames = storeRefs.Select(s => s.StoreName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (storeNames.Count == 0) return summary;

        var allPlans = await _db.StoreActionPlans.Where(p => storeNames.Contains(p.StoreName)).ToListAsync();
        var activePlans = allPlans.Where(p => p.Status == "Active").ToList();
        var resolvedPlans = allPlans.Where(p => p.Status == "Resolved" && p.ResolvedAt.HasValue).ToList();
        var now = DateTime.UtcNow;
        var bands = await _severityConfig.GetAsync();

        summary.TotalActive = activePlans.Count;
        summary.OpenedThisMonth = allPlans.Count(p => p.CreatedAt.Year == now.Year && p.CreatedAt.Month == now.Month);
        summary.ResolvedThisMonth = resolvedPlans.Count(p => p.ResolvedAt!.Value.Year == now.Year && p.ResolvedAt.Value.Month == now.Month);
        summary.AvgDaysToResolution = resolvedPlans.Count > 0
            ? Math.Round(resolvedPlans.Average(p => (p.ResolvedAt!.Value - p.CreatedAt).TotalDays), 1)
            : null;

        var storeGroups = allPlans.GroupBy(p => p.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var activePlanIds = activePlans.Select(p => p.Id).ToList();
        var recsByActivePlan = (await _db.ActionPlanRecommendations.Where(r => activePlanIds.Contains(r.StoreActionPlanId)).ToListAsync())
            .GroupBy(r => r.StoreActionPlanId).ToDictionary(g => g.Key, g => g.ToList());

        int chronicCount = 0, stalledCount = 0, criticalCount = 0;
        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in activePlans)
        {
            if (storeGroups.TryGetValue(plan.StoreName, out var histCount) && histCount > 1) chronicCount++;
            var recs = recsByActivePlan.TryGetValue(plan.Id, out var r) ? r : new List<ActionPlanRecommendation>();
            var distinctSignals = recs.Select(x => x.SignalCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (ComputeSeverity(plan.Status, distinctSignals, bands) == "Critical") criticalCount++;

            var ageDays = (int)(now - plan.CreatedAt).TotalDays;
            var tasksCompleted = recs.Count(x => x.IsCompleted);
            if (ageDays >= StalledAgeDaysThreshold && tasksCompleted == 0) stalledCount++;

            foreach (var cat in recs.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase))
                categoryCounts[cat] = categoryCounts.GetValueOrDefault(cat) + 1;
        }
        summary.ChronicCount = chronicCount;
        summary.StalledCount = stalledCount;
        summary.CriticalCount = criticalCount;
        summary.TopReasons = categoryCounts.OrderByDescending(kv => kv.Value)
            .Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }).ToList();

        var omByStore = await GetOperationManagerByStoreAsync(
            activePlans.Select(p => p.StoreName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        summary.ByRegion = activePlans
            .Select(p => omByStore.TryGetValue(p.StoreName, out var om) ? om : "")
            .Where(om => !string.IsNullOrWhiteSpace(om))
            .GroupBy(om => om, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();

        // Monthly trend — last 6 calendar months, opened vs. resolved.
        var months = Enumerable.Range(0, 6)
            .Select(i => now.AddMonths(-i))
            .Select(d => (d.Year, d.Month))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();
        summary.MonthlyTrend = months.Select(p => new ActionCenterTrendPointDto
        {
            Label = new DateOnly(p.Year, p.Month, 1).ToString("MMM yy"),
            Opened = allPlans.Count(x => x.CreatedAt.Year == p.Year && x.CreatedAt.Month == p.Month),
            Resolved = resolvedPlans.Count(x => x.ResolvedAt!.Value.Year == p.Year && x.ResolvedAt.Value.Month == p.Month),
        }).ToList();

        return summary;
    }

    public async Task<StoreActionPlanDto?> GetActionCenterDetailAsync(string storeName, string role, string? email)
    {
        var dto = await GetForStoreAsync(storeName, role, email);
        if (dto == null || dto.Status == "None") return dto;

        var allPlans = await _db.StoreActionPlans.Where(p => p.StoreName == storeName).ToListAsync();
        dto.HistoricalPlanCount = allPlans.Count;
        dto.IsChronic = allPlans.Count > 1;

        // Same "most recent plan" selection GetForStoreAsync itself used.
        var plan = allPlans.OrderByDescending(p => p.CreatedAt).First();
        dto.AssignedToName = plan.AssignedToName;
        dto.TargetResolutionDate = plan.TargetResolutionDate;
        dto.ClosedByName = plan.ClosedByName;
        dto.ManualCloseReason = plan.ManualCloseReason;
        dto.CanManage = role == "Admin";

        var distinctSignals = dto.Recommendations.Select(r => r.SignalCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        dto.Severity = ComputeSeverity(dto.Status, distinctSignals, await _severityConfig.GetAsync());
        var (reportingQuarter, reportingYear) = GetReportingQuarter(plan.CreatedAt);
        dto.ReportingQuarter = reportingQuarter;
        dto.ReportingYear = reportingYear;
        var (targetWindowType, targetWindowDays) = ComputeTargetWindow(plan.CreatedAt, plan.TargetResolutionDate);
        dto.TargetWindowType = targetWindowType;
        dto.TargetWindowDays = targetWindowDays;

        var snapshots = await _db.ActionPlanMetricSnapshots
            .Where(s => s.StoreActionPlanId == plan.Id)
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToListAsync();
        dto.MetricSnapshots = snapshots.Select(s => new ActionCenterMetricSnapshotDto
        {
            Label = new DateOnly(s.Year, s.Month, 1).ToString("MMM yy"),
            Month = s.Month,
            Year = s.Year,
            TurnoverRate = s.TurnoverRate,
            EarlyLeaverRate = s.EarlyLeaverRate,
            RetentionRate = s.RetentionRate,
            SignalCount = s.SignalCount,
        }).ToList();

        var ageDays = (int)((dto.Status == "Resolved" && dto.ResolvedAt.HasValue ? dto.ResolvedAt.Value : DateTime.UtcNow) - dto.CreatedAt).TotalDays;
        var tasksCompleted = dto.Recommendations.Count(r => r.IsCompleted);
        dto.IsStalled = dto.Status == "Active" && ageDays >= StalledAgeDaysThreshold && tasksCompleted == 0;

        return dto;
    }

    public async Task<bool> ToggleRecommendationAsync(int recommendationId, bool isCompleted, string role, string? email, string actorName)
    {
        var rec = await _db.ActionPlanRecommendations.FindAsync(recommendationId);
        if (rec == null) return false;
        var plan = await _db.StoreActionPlans.FindAsync(rec.StoreActionPlanId);
        if (plan == null) return false;

        if (role != "Admin")
        {
            var effectiveRole = await _actionPlanRoles.GetEffectiveRoleAsync(plan.StoreName);
            if (effectiveRole == null || role != effectiveRole) return false;
            // Own-store write check — never widened by the Action Center area toggle.
            if (!await _storeAccess.CanManageStoreAsync(role, email, plan.StoreName)) return false;
        }

        rec.IsCompleted = isCompleted;
        rec.CompletedAt = isCompleted ? DateTime.UtcNow : null;
        rec.CompletedByName = isCompleted ? actorName : null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetAssignmentAsync(string storeName, string? assignedToName, DateOnly? targetResolutionDate, string role)
    {
        if (role != "Admin") return false;
        var plan = await _db.StoreActionPlans.FirstOrDefaultAsync(p => p.StoreName == storeName && p.Status == "Active");
        if (plan == null) return false;

        plan.AssignedToName = string.IsNullOrWhiteSpace(assignedToName) ? null : assignedToName.Trim();
        plan.TargetResolutionDate = targetResolutionDate;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(bool success, string message)> ManualCloseAsync(string storeName, string reason, string role, string closedByName)
    {
        if (role != "Admin") return (false, "Only an Admin can manually close a plan.");
        if (string.IsNullOrWhiteSpace(reason)) return (false, "A reason is required.");
        var plan = await _db.StoreActionPlans.FirstOrDefaultAsync(p => p.StoreName == storeName && p.Status == "Active");
        if (plan == null) return (false, "No active plan for this store.");

        plan.Status = "Resolved";
        plan.ResolvedAt = DateTime.UtcNow;
        plan.ResolvedReason = "Manual_Override";
        plan.ClosedByName = closedByName;
        plan.ManualCloseReason = reason.Trim();
        await _db.SaveChangesAsync();
        return (true, "Plan closed.");
    }
}
