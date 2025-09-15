namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IMasterWalkForwardOrchestrator
    {
        Task StartInitialTrainingPhase(Guid sessionId, string strategyName, DateTime startDate, DateTime endDate);

        //Task ExecuteCycle(Guid sessionId, DateTime currentTradingPeriodStart);
    }
}
