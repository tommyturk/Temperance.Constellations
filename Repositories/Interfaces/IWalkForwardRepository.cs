using Temperance.Constellations.Models;
using Temperance.Ephemeris.Models.Backtesting;
using Temperance.Ephemeris.Models.Constellations;

namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface IWalkForwardRepository
    {
        Task<WalkForwardSessionModel> GetSessionAsync(Guid sessionId);
        Task UpdateSessionStatusAsync(Guid sessionId, string status);
        Task<IEnumerable<WalkForwardSleeve>> GetActiveSleeveAsync(Guid sessionId, DateTime asOfDate);
        Task<CycleTracker> GetCycleTrackerAsync(Guid cycleTrackerId);
        Task CreateCycleTracker(CycleTracker cycle);
        Task<CycleTracker> SignalCompletionAndCheckIfReady(Guid cycleTrackerId, BacktestType backtestType);
        Task UpdateSessionCapitalAsync(Guid sessionId, decimal? finalCapital);
        Task<BacktestRunModel> GetLatestRunForSessionAsync(Guid sessionId);
        Task<StrategyOptimizedParameters> GetOptimizedParametersForSymbol(Guid sessionId, string symbol, DateTime dateTime);
        Task<List<CycleTracker>> GetCycleTrackersForSession(Guid sessionId);
        Task UpdateCurrentCapital(Guid sessionId, decimal profitLoss);
        Task<PortfolioState?> GetLatestPortfolioStateAsync(Guid sessionId);

        /// <summary>
        /// Saves the results of a single walk-forward cycle and the portfolio's state at the end of it.
        /// </summary>
        Task SaveCycleResultsAsync(Guid sessionId, Guid cycleRunId, BacktestResult cycleResult, PortfolioStateModel portfolioState);
        
        /// <summary>
        /// Saves a failure record for a specific walk-forward cycle.
        /// </summary>
        Task SaveCycleFailureAsync(Guid sessionId, Guid cycleRunId, string errorMessage);
    }
}

