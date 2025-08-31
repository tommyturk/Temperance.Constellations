using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;

namespace TradingApp.src.Core.Services.Interfaces
{
    public interface ITradeService
    {
        Task<int> SaveTradeAsync(Trade trade);
        Task<int> ExecuteOrderAsync(Order order);
        Task<int> UpdatePositionAsync(Position position);
        Task<int> LogStrategyAsync(StrategyLog log);
        Task InitializeBacktestRunAsync(BacktestConfiguration config, Guid runId);
        Task InitializePairBacktestRunAsync(PairsBacktestConfiguration config, Guid runId);
        Task UpdateBacktestRunStatusAsync(Guid runId, string status, string? errorMessage = null);
        Task SaveTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);
        Task FinalizeBacktestRunAsync(Guid runId, BacktestResult result);
        Task SaveBacktestResults(Guid runId, IEnumerable<TradeSummary> trades);
        Task SaveBacktestResults(Guid runId, BacktestResult backtestResult, string symbol, string interval);
        Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult metrics, double initialCapital);
        Task SaveOrUpdateBacktestTrade(TradeSummary trade);
        Task<(double Cash, List<Position> OpenPositions)?> GetLatestPortfolioStateAsync(Guid sessionId);
        
        Task<IEnumerable<WalkForwardSleeve>> GetSleevesForSessionAsync(Guid sessionId, DateTime tradingPeriodStartDate);
        Task<WalkForwardSession?> GetSessionAsync(Guid sessionId);
        Task UpdateSessionCapitalAsync(Guid sessionId, double newCapital);
    }
}
