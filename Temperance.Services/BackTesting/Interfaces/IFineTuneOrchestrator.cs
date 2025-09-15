namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IFineTuneOrchestrator
    {
        Task ExecuteFineTune(Guid sessionId, DateTime backtestMonthEndDate);
    }
}
