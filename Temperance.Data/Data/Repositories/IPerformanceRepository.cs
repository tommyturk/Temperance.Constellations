using Temperance.Data.Models.Backtest;

namespace Temperance.Data.Data.Repositories
{
    public interface IPerformanceRepository
    {
        Task<IEnumerable<SleeveComponent>> GetSleeveComponentsAsync(Guid runId);

        Task<IEnumerable<ShadowPerformance>> GetShadowPerformanceAsync(Guid runId);

        Task SaveSleeveComponentsAsync(IEnumerable<SleeveComponent> components);

        Task SaveShadowPerformanceAsync(IEnumerable<ShadowPerformance> performances);
    }
}
