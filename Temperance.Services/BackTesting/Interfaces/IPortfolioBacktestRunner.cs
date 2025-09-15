namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPortfolioBacktestRunner
    {
        Task ExecuteBacktest(Guid sessionId, DateTime oosStartDate);
    }
}
