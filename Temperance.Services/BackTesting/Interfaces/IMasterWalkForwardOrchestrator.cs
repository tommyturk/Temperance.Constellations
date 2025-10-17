using Temperance.Data.Models.Backtest;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IMasterWalkForwardOrchestrator
    {
        Task InitiateCycle(Guid sessionId, DateTime cycleStartDate);
        Task SignalBacktestCompletion(Guid cycleTrackerId, BacktestType backtestType);
    }
}
