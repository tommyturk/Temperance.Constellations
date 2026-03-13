namespace Temperance.Constellations.BackTesting.Interfaces
{
    public interface IInitialTrainingOrchestrator
    {
        Task StartInitialTraining(Guid sessionId);
    }
}
