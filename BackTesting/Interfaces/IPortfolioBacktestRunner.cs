namespace Temperance.Constellations.BackTesting.Interfaces
{
    public interface IPortfolioBacktestRunner
    {
        Task ExecuteBacktest(Guid sessionId, DateTime oosStartDate, DateTime oosEndDate);
    }
}
