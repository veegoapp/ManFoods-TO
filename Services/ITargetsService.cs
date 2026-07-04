using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface ITargetsService
{
    Task<TargetsViewModel> GetAsync();
    Task SetAsync(double? turnoverRateTarget, double? retention90Target);
}
