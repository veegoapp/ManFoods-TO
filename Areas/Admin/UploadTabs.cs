namespace MvcApp.Areas.Admin;

/// <summary>The tabs on the Data Management (Uploads) page and the upload-history
/// kind each one shows.</summary>
public static class UploadTabs
{
    public const string Monthly = "monthly";
    public const string Exit = "exit";
    public const string JobProjections = "jobproj";

    /// <summary>Unknown/missing values fall back to the first tab.</summary>
    public static string Normalize(string? tab) => tab switch
    {
        Exit => Exit,
        JobProjections => JobProjections,
        _ => Monthly,
    };

    public static string KindOf(string tab) => Normalize(tab) switch
    {
        Exit => "exit_interviews",
        JobProjections => "job_projections",
        _ => "period",
    };
}
