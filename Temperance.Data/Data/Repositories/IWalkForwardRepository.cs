using Temperance.Data.Models.Backtest;

namespace Temperance.Conductor.Repository.Interfaces
{
    public interface IWalkForwardRepository
    {
        Task<WalkForwardSession> GetSessionAsync(Guid sessionId);
        Task UpdateSessionStatusAsync(Guid sessionId, string status);
        Task<IEnumerable<WalkForwardSleeve>> GetSleevesByBatchAsync(Guid sessionId, DateTime tradingPeriodStartDate);
        Task SetActiveSleeveAsync(Guid sessionId, DateTime tradingPeriodStartDate, IEnumerable<string> symbols);
        Task<Dictionary<string, Dictionary<string, object>>> GetLatestParametersForSleeveAsync(Guid sessionId, IEnumerable<string> symbols);
        Task<IEnumerable<WalkForwardSleeve>> GetActiveSleeveAsync(Guid sessionId, DateTime asOfDate);
        Task CreateSleeveBatchAsync(List<WalkForwardSleeve> sleeves);
        Task<IEnumerable<OptimizationJob>> GetCompletedJobsForSessionAsync(Guid sessionId);
        Task<IEnumerable<StrategyOptimizedParameters>> GetResultsByKeysAsync(List<string> resultKeys, Guid sessionId);
        Task<HashSet<string>> GetSleeveSymbolsForPeriodAsync(Guid sessionId, DateTime tradingPeriodStartDate);

        Task UpdateSessionCapitalAsync(Guid sessionId, double? finalCapital);
        Task<BacktestRun> GetLatestRunForSessionAsync(Guid sessionId);
        Task<StrategyOptimizedParameters> GetOptimizedParametersForSymbol(Guid sessionId, string symbol, DateTime dateTime);
    }
}

