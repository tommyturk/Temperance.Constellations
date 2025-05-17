using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Trading;

namespace Temperance.Data.Data.Repositories.Trade.Interfaces
{
    public interface ITradeRepository
    {
        // --- Existing Methods for Live/Simulated Trading ---
        Task<int> SaveTradeAsync(Models.Trading.Trade trade);
        Task<int> ExecuteOrderAsync(Models.Trading.Order order);
        Task<int> UpdatePositionAsync(Models.Trading.Position position);
        Task<int> LogStrategyAsync(StrategyLog log);
        Task CheckTradeExitsAsync(); // Keep if used elsewhere, but likely not by backtester

        // --- New Methods for Backtesting Persistence ---
        Task InitializeBacktestRunAsync(Guid runId, BacktestConfiguration config);
        Task UpdateBacktestRunStatusAsync(Guid runId, string status, DateTime timestamp, string? errorMessage = null);
        Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);
        Task UpdateBacktestRunTotalsAsync(Guid runId, BacktestResult result); // Replaces Finalize concept
        Task<BacktestRun?> GetBacktestRunAsync(Guid runId); // Fetch run summary data
        Task<IEnumerable<TradeSummary>> GetBacktestTradesAsync(Guid runId); // Fetch trades for a run
        // Optional: Task<string?> GetBacktestRunStatusOnlyAsync(Guid runId); if needed
    }
}
