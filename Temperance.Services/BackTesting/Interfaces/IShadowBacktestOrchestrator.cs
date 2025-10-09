namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IShadowBacktestOrchestrator
    {
        Task Execute(Guid sessionId, Guid runId, List<string> shadowUniverse, DateTime startDate, DateTime endDate);
    }
}
