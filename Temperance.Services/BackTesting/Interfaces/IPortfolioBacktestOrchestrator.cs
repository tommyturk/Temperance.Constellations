namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPortfolioBacktestOrchestrator
    {
        Task ExecuteNextPeriod(Guid cycleTrackerId, Guid sessionId, DateTime oosStartDate, DateTime oosEndDate);
    }
}
