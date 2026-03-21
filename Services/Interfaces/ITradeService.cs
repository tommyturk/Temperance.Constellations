using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Strategy;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Backtesting;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Models.Trading;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface ITradeService
    {
        Task<int> SaveTradeAsync(Trade trade);
        Task<int> ExecuteOrderAsync(Order order);
        Task<int> UpdatePositionAsync(Models.Trading.Position position);
        Task<int> LogStrategyAsync(StrategyLog log);
        Task InitializeBacktestRunAsync(BacktestConfiguration config, Guid runId);
        Task InitializePairBacktestRunAsync(PairsBacktestConfiguration config, Guid runId);
        Task UpdateBacktestRunStatusAsync(Guid runId, string status, string? errorMessage = null);
        Task SaveTradesAsync(Guid runId, IEnumerable<TradeSummary> trades);
        Task FinalizeBacktestRunAsync(Guid runId, BacktestResult result);
        Task SaveBacktestResults(Guid runId, IEnumerable<TradeSummary> trades);
        Task SaveBacktestResults(Guid runId, BacktestResult backtestResult, string symbol, string interval);
        Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult metrics, decimal initialCapital);
        Task SaveOrUpdateBacktestTrade(TradeSummary trade);
        Task<(decimal Cash, List<Models.Trading.Position> OpenPositions)?> GetLatestPortfolioStateAsync(Guid sessionId);
        
        Task<IEnumerable<WalkForwardSleeve>> GetSleevesForSessionAsync(Guid sessionId, DateTime tradingPeriodStartDate);
        Task<WalkForwardSessionModel?> GetSessionAsync(Guid sessionId);
        Task UpdateSessionCapitalAsync(Guid sessionId, decimal newCapital);
        Task<IEnumerable<BacktestRunModel>> GetBacktestRunsForSessionAsync(Guid sessionId, DateTime startDate, DateTime endDate);
        Task SaveSleevesAsync(IEnumerable<WalkForwardSleeve> sleeves);

        Task<TradeSummary?> GetActiveTradeForPositionAsync(Guid positionId);

        Task SaveTradesBulkAsync(IEnumerable<TradeSummary> trades);
    }
}
