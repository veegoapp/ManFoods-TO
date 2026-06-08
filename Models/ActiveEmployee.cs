using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

[Table("active_employees")]
public class ActiveEmployee
{
    [Column("id")]
    public int Id { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("employee_id")]
    public string EmployeeId { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("store")]
    public string Store { get; set; } = "";

    [Column("job_title")]
    public string JobTitle { get; set; } = "";

    [Column("grade")]
    public string Grade { get; set; } = "";

    [Column("payroll_group")]
    public string PayrollGroup { get; set; } = "";

    [Column("cost_center")]
    public string CostCenter { get; set; } = "";

    [Column("gender")]
    public string Gender { get; set; } = "";

    [Column("hire_date")]
    public DateOnly? HireDate { get; set; }
}
