namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPortfolioBacktestOrchestrator
    {
        Task ExecuteBacktest(Guid sessionId, DateTime startDate, DateTime totalEndDate);

        Task ExecuteCycle(Guid sessionId, DateTime currentOosStartDate, DateTime totalEndDate);

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
