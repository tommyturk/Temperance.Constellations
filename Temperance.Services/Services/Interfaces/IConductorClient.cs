namespace Temperance.Services.Services.Interfaces
{
    public interface IConductorClient
    {
        Task NotifyBacktestCompleteAsync(BacktestCompletionPayload payload);
    }
}
