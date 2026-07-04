namespace MvcApp.Models.ViewModels;

public class AiCostRates
{
    public double InputPricePerMillion { get; set; }
    public double OutputPricePerMillion { get; set; }
}

public class AiUsageSummary
{
    public int QuestionsToday { get; set; }
    public int QuestionsLast7Days { get; set; }
    public int QuestionsLast30Days { get; set; }
    public long TokensToday { get; set; }
    public long TokensLast30Days { get; set; }
    public double EstimatedCostToday { get; set; }
    public double EstimatedCostLast30Days { get; set; }
}

public class AiUsageTrendPoint
{
    public string Label { get; set; } = "";
    public int Questions { get; set; }
    public long Tokens { get; set; }
}

public class AiTopUserRow
{
    public int UserId { get; set; }
    public string Email { get; set; } = "";
    public int Questions { get; set; }
    public long Tokens { get; set; }
    public double EstimatedCost { get; set; }
}
