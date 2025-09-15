using Temperance.Data.Models.Backtest;

namespace Temperance.Conductor.Repository.Interfaces
{
    public interface IWalkForwardRepository
    {
        Task<WalkForwardSession> GetSessionAsync(Guid sessionId);
        Task UpdateSessionStatusAsync(Guid sessionId, string status);
        Task<IEnumerable<WalkForwardSleeve>> GetSleevesByBatchAsync(Guid sessionId, DateTime tradingPeriodStartDate);
        Task SetActiveSleeveAsync(Guid sessionId, DateTime tradingPeriodStartDate, IEnumerable<string> symbols);
        Task<IEnumerable<WalkForwardSleeve>> GetActiveSleeveAsync(Guid sessionId, DateTime asOfDate);
    }
}

