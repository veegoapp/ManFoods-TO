using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>The kinds of coverage an Admin can give a Trainer. Each person kind is matched against the
/// matching email column of the latest Store Reference period; <see cref="Store"/> is one fixed store name.</summary>
public static class CoverageKinds
{
    public const string Director = "od";
    public const string Manager = "om";
    public const string SeniorConsultant = "soc";
    public const string Consultant = "oc";
    public const string HeadManager = "hm";
    public const string Store = "store";

    /// <summary>The person levels, top of the hierarchy first.</summary>
    public static readonly IReadOnlyList<string> PersonKinds = new[] { Director, Manager, SeniorConsultant, Consultant, HeadManager };

    public static bool IsValid(string? kind) => kind == Store || (kind != null && PersonKinds.Contains(kind));
}

/// <summary>
/// One thing a Trainer covers: a whole person (every store that person has in the latest Store Reference
/// period, so a monthly re-upload moves the stores automatically) or one fixed store. For a person kind
/// <see cref="Value"/> is the email, trimmed and lower-cased; for <see cref="CoverageKinds.Store"/> it is
/// the store name as written in Store Reference.
/// </summary>
[Table("trainer_coverage")]
public class TrainerCoverage
{
    [Column("id")]
    public int Id { get; set; }

    [Column("trainer_email")]
    public string TrainerEmail { get; set; } = "";

    [Column("kind")]
    public string Kind { get; set; } = "";

    [Column("value")]
    public string Value { get; set; } = "";
}

/// <summary>Per-Trainer setting: whether the Trainer may also write (Action Plan notes and tasks)
/// or is read-only. A Trainer without a row is read-only.</summary>
[Table("trainer_settings")]
public class TrainerSetting
{
    [Column("trainer_email")]
    public string TrainerEmail { get; set; } = "";

    [Column("can_edit")]
    public bool CanEdit { get; set; }
}
