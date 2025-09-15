namespace Temperance.Services.BackTesting.Interfaces
{
    public interface ISleeveSelectionOrchestrator
    {
        Task SelectInitialSleeve(Guid sessionId, DateTime inSampleEndDate);
        Task ReselectAnnualSleeve(Guid sessionId, DateTime yearEnd);

    }
}
