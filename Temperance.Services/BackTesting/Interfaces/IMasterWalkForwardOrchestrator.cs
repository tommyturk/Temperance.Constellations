namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IMasterWalkForwardOrchestrator
    {
        Task ExecuteCycle(Guid sessionId, DateTime startDate);
    }
}
