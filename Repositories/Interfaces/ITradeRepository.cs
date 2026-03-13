using Temperance.Constellations.Models;
using Temperance.Ephemeris.Models.Backtesting;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Trading;
namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface ITradeRepository
    {
        Task<int> SaveTradeAsync(Ephemeris.Models.Trading.Trade trade);
        Task<int> ExecuteOrderAsync(Order order);
        Task<int> UpdatePositionAsync(Models.Trading.Position position);
        Task<int> LogStrategyAsync(StrategyLog log);
        Task CheckTradeExitsAsync(); 
        Task InitializeBacktestRunAsync(Guid runId, BacktestConfiguration config);
        Task UpdateBacktestRunStatusAsync(Guid runId, string status, DateTime timestamp, string? errorMessage = null);
        Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);
        Task UpdateBacktestRunTotalsAsync(Guid runId, BacktestResult result); 
        Task<BacktestRunModel?> GetBacktestRunAsync(Guid runId); 
        Task<IEnumerable<TradeSummary>> GetBacktestTradesAsync(Guid runId); 

        Task SaveOrUpdateBacktestTradeAsync(TradeSummary trade);
        Task SavePortfolioStateAsync(Guid runId, DateTime asOfDate, decimal cash, IEnumerable<Models.Trading.Position> openPositions);
        Task<(decimal Cash, List<Models.Trading.Position> OpenPositions)?> GetLatestPortfolioStateAsync(Guid sessionId);

        Task<IEnumerable<BacktestRunModel>> GetBacktestRunsForSessionAsync(Guid sessionId, DateTime startDate, DateTime endDate);
        Task SaveSleevesAsync(IEnumerable<WalkForwardSleeve> sleeves);
        Task<IEnumerable<WalkForwardSleeve>> GetSleevesForSessionAsync(Guid sessionId, DateTime tradingPeriodStartDate);
        Task<WalkForwardSessionModel?> GetSessionAsync(Guid sessionId);
        Task UpdateSessionCapitalAsync(Guid sessionId, decimal newCapital);
        Task<TradeSummary?> GetActiveTradeByPositionIdAsync(Guid positionId);
    }
}
