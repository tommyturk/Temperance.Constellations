using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Trading;

namespace Temperance.Data.Data.Repositories.Trade.Interfaces
{
    public interface ITradeRepository
    {
        Task<int> SaveTradeAsync(Models.Trading.Trade trade);
        Task<int> ExecuteOrderAsync(Models.Trading.Order order);
        Task<int> UpdatePositionAsync(Models.Trading.Position position);
        Task<int> LogStrategyAsync(StrategyLog log);
        Task CheckTradeExitsAsync(); 
        Task InitializeBacktestRunAsync(Guid runId, BacktestConfiguration config);
        Task UpdateBacktestRunStatusAsync(Guid runId, string status, DateTime timestamp, string? errorMessage = null);
        Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);
        Task UpdateBacktestRunTotalsAsync(Guid runId, BacktestResult result); 
        Task<BacktestRun?> GetBacktestRunAsync(Guid runId); 
        Task<IEnumerable<TradeSummary>> GetBacktestTradesAsync(Guid runId); 

        Task SaveOrUpdateBacktestTradeAsync(TradeSummary trade);
        Task SavePortfolioStateAsync(Guid runId, DateTime asOfDate, double cash, IEnumerable<Position> openPositions);
        Task<(double Cash, List<Position> OpenPositions)?> GetLatestPortfolioStateAsync(Guid sessionId);

        Task<IEnumerable<BacktestRun>> GetBacktestRunsForSessionAsync(Guid sessionId, DateTime startDate, DateTime endDate);
        Task SaveSleevesAsync(IEnumerable<WalkForwardSleeve> sleeves);
        Task<IEnumerable<WalkForwardSleeve>> GetSleevesForSessionAsync(Guid sessionId, DateTime tradingPeriodStartDate);
        Task<WalkForwardSession?> GetSessionAsync(Guid sessionId);
        Task UpdateSessionCapitalAsync(Guid sessionId, double newCapital);
    }
}
