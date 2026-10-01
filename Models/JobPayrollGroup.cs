using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// The payroll group of a job title. This list is the reference for payroll groups in Workforce
/// Planning (chart, table, report); a job that is not on it falls back to the group most of its
/// employees on the roster are in. Replaced as a whole by an upload in Data Management.
/// </summary>
[Table("job_payroll_groups")]
public class JobPayrollGroup
{
    [Column("id")]
    public int Id { get; set; }

    [Column("job_title")]
    public string JobTitle { get; set; } = "";

    [Column("payroll_group")]
    public string PayrollGroup { get; set; } = "";

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
