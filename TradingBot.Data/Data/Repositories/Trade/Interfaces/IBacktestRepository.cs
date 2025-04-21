using TradingBot.Data.Models.Backtest;
using TradingBot.Data.Models.Trading;

namespace TradingBot.Data.Data.Repositories.Trade.Interfaces
{
    public interface IBacktestRepository
    {
        Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);

        Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult result, decimal initialCapital);
    }
}
