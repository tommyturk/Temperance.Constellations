using Temperance.Constellations.Models;

namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface IPerformanceRepository
    {
        Task<IEnumerable<SleeveComponent>> GetSleeveComponentsAsync(Guid runId);

        Task<IEnumerable<ShadowPerformance>> GetShadowPerformanceAsync(Guid runId);

        Task SaveSleeveComponentsAsync(IEnumerable<SleeveComponent> components);

        Task SaveShadowPerformanceAsync(IEnumerable<ShadowPerformance> performances);
    }
}
