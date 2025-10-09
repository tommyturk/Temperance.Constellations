namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IInitialTrainingOrchestrator
    {
        Task StartInitialTraining(Guid sessionId);
    }
}
