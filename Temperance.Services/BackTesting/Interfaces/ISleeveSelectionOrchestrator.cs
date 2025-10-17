namespace Temperance.Services.BackTesting.Interfaces
{
    public interface ISleeveSelectionOrchestrator
    {
        Task ReselectAnnualSleeve(Guid cycleTrackerId, Guid sessionId, DateTime yearEnd);
    }
}
