using Temperance.Constellations.Models;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IConductorClient
    {
        Task NotifyBacktestCompleteAsync(BacktestCompletionPayload payload);

        Task DispatchOptimizationBatchAsync(OptimizationBatchRequest request);
    }
}
