using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>Per-user, per-day count of AI Assistant questions, used to
/// enforce the admin-configurable daily question limit.</summary>
[Table("ai_usage_daily")]
public class AiUsageDaily
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("usage_date")]
    public DateOnly UsageDate { get; set; }

    [Column("question_count")]
    public int QuestionCount { get; set; }

    [Column("prompt_tokens")]
    public long PromptTokens { get; set; }

    [Column("completion_tokens")]
    public long CompletionTokens { get; set; }
}
