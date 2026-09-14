using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One store's forward-looking staffing plan for a month/year: the headcount
/// the business expects that store to require. Compared against the store's
/// current active headcount to surface a projected staffing gap — the only
/// forward-looking input the Store Health engine has (every other pillar is
/// backward-looking). Uploaded independently of the three period files (like
/// exit interviews), so it is optional: a store with no projection row simply
/// contributes no Workforce-Outlook signal rather than being penalised.
/// </summary>
[Table("workforce_projections")]
public class WorkforceProjection
{
    [Column("id")]
    public int Id { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    /// <summary>Headcount the store is planned/required to operate at for this
    /// period. The gap vs. current active headcount is what the engine reads.</summary>
    [Column("projected_headcount")]
    public int ProjectedHeadcount { get; set; }

    /// <summary>Optional planned new hires for the period — lets the outlook
    /// distinguish "short but hiring" from "short with no pipeline". 0 when the
    /// upload did not provide it.</summary>
    [Column("planned_hires")]
    public int PlannedHires { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
