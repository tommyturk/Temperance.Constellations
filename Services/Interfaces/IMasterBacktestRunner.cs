namespace Temperance.Constellations.Services.Interfaces
{
    public interface IMasterBacktestRunner
    {
        Task ExecuteFullSessionAsync(Guid sessionId, DateTime sessionStartDate, DateTime sessionEndDate);
    }
}
