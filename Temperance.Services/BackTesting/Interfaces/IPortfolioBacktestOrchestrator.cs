namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPortfolioBacktestOrchestrator
    {
        Task ExecuteNextPeriod(
            Guid cycleTrackerId,
            Guid sessionId,
            Guid portfolioBacktestRunId,
            List<string> activeUniverse,
            DateTime inSampleStartDate,
            DateTime inSampleEndDate,
            DateTime oosStartDate,
            DateTime oosEndDate);
    }
}
