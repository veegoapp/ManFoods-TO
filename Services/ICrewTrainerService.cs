using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface ICrewTrainerService
{
    /// <summary>The Crew Trainers page for one month that has an uploaded trainer list (defaults to the
    /// latest): trainers against the Crew Trainer projection per store, the new hires each store has to
    /// train, the trend over the year, and who joined or left the list since the previous one.
    /// Scoped to the caller's stores and the leadership filters.</summary>
    Task<CrewTrainerDto> GetAsync(int? year, int? month, string? stores, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);
}
