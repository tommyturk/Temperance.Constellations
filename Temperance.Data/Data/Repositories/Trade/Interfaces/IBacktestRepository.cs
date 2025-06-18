using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Trading;

namespace Temperance.Data.Data.Repositories.Trade.Interfaces
{
    public interface IBacktestRepository
    {
        Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);

        Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult result, double initialCapital);
    }
}
