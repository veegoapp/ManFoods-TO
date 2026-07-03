using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One Microsoft Forms exit-interview submission. Deliberately holds no name
/// or national ID — EmployeeId is kept only to resolve Store/StoreLeader/OC/OM
/// at upload time and is never surfaced in any view or API response.
/// </summary>
[Table("exit_interviews")]
public class ExitInterview
{
    [Column("id")]
    public int Id { get; set; }

    [Column("forms_response_id")]
    public string FormsResponseId { get; set; } = "";

    [Column("employee_id")]
    public string EmployeeId { get; set; } = "";

    [Column("store")]
    public string Store { get; set; } = "";

    [Column("store_leader")]
    public string StoreLeader { get; set; } = "";

    [Column("operation_consultant")]
    public string OperationConsultant { get; set; } = "";

    [Column("operation_manager")]
    public string OperationManager { get; set; } = "";

    [Column("job_title")]
    public string JobTitle { get; set; } = "";

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("submitted_at")]
    public DateTime? SubmittedAt { get; set; }

    // ── Choice questions → charted ──────────────────────────
    [Column("reason_for_leaving")]
    public string ReasonForLeaving { get; set; } = "";

    [Column("would_return")]
    public string WouldReturn { get; set; } = "";

    [Column("overall_experience")]
    public string OverallExperience { get; set; } = "";

    [Column("workload_condition")]
    public string WorkloadCondition { get; set; } = "";

    [Column("fair_treatment")]
    public string FairTreatment { get; set; } = "";

    [Column("encourage_opinions")]
    public string EncourageOpinions { get; set; } = "";

    [Column("complaints_handling")]
    public string ComplaintsHandling { get; set; } = "";

    [Column("benefits_match")]
    public string BenefitsMatch { get; set; } = "";

    [Column("teamwork")]
    public string Teamwork { get; set; } = "";

    [Column("communication")]
    public string Communication { get; set; } = "";

    [Column("task_fit")]
    public string TaskFit { get; set; } = "";

    [Column("training")]
    public string Training { get; set; } = "";

    [Column("feedback")]
    public string Feedback { get; set; } = "";

    [Column("use_personal_abilities")]
    public string UsePersonalAbilities { get; set; } = "";

    // ── Free-text questions → anonymous comments list ───────
    [Column("reason_other_text")]
    public string? ReasonOtherText { get; set; }

    [Column("work_pressure_reason_text")]
    public string? WorkPressureReasonText { get; set; }

    [Column("what_would_change_text")]
    public string? WhatWouldChangeText { get; set; }

    [Column("what_learned_text")]
    public string? WhatLearnedText { get; set; }

    [Column("final_comments_text")]
    public string? FinalCommentsText { get; set; }
}
