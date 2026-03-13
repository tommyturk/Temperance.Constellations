using Temperance.Constellations.Models;

namespace Temperance.Constellations.BackTesting.Interfaces
{
    public interface IMasterWalkForwardOrchestrator
    {
        Task InitiateCycle(Guid sessionId, DateTime cycleStartDate);
        Task SignalBacktestCompletion(Guid cycleTrackerId, BacktestType backtestType);
    }
}
