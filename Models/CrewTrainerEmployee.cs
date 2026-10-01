using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// An employee who receives the Crew Trainer allowance in one month. The roster has no
/// "Crew Trainer" job (trainers are Crew with an allowance), so this monthly list is what
/// Workforce Planning counts as the actual Crew Trainer headcount of each store.
/// A re-upload for a month replaces that whole month.
/// </summary>
[Table("crew_trainer_employees")]
public class CrewTrainerEmployee
{
    [Column("id")]
    public int Id { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("employee_id")]
    public string EmployeeId { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("job_title")]
    public string JobTitle { get; set; } = "";

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
