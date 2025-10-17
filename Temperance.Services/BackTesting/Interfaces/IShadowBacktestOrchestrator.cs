namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IShadowBacktestOrchestrator
    {
        Task Execute(Guid cycleTrackerId, Guid sessionId, Guid runId, List<string> shadowUniverse, DateTime startDate, DateTime endDate);
    }
}
