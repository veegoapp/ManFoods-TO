using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One Operation Consultant a Trainer is responsible for. A Trainer sees the stores those
/// consultants manage in the latest Store Reference period (see StoreAccessService), so a
/// monthly re-upload moves the stores automatically while the pick itself stays put.
/// Both emails are stored trimmed and lower-cased.
/// </summary>
[Table("trainer_consultants")]
public class TrainerConsultant
{
    [Column("id")]
    public int Id { get; set; }

    [Column("trainer_email")]
    public string TrainerEmail { get; set; } = "";

    [Column("consultant_email")]
    public string ConsultantEmail { get; set; } = "";
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
