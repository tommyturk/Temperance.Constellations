namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPortfolioBacktestOrchestrator
    {
        Task ExecuteNextPeriod(Guid sessionId, DateTime oosStartDate);
    }
}
