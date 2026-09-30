using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Projected headcount for one job title in one store for a month/year.
/// Uploaded as a single yearly workbook (one sheet per month, stores as rows,
/// job titles as columns) — a re-upload for a year replaces that whole year.
/// Independent of the three period files.
/// </summary>
[Table("job_headcount_projections")]
public class JobHeadcountProjection
{
    [Column("id")]
    public int Id { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    [Column("job_title")]
    public string JobTitle { get; set; } = "";

    [Column("projected_headcount")]
    public int ProjectedHeadcount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
