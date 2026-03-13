using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Trading;

namespace Temperance.Constellations.Repositories.Interfaces.Trade.Interfaces
{
    public interface IBacktestRepository
    {
        Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);

        Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult result, decimal initialCapital);
    }
}
