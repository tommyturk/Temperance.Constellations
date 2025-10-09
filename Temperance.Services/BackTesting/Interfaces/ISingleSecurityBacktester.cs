using Temperance.Data.Models.Backtest;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface ISingleSecurityBacktester
    {
        Task<PerformanceSummary> RunAsync(WalkForwardSession session, string symbol, DateTime startDate, DateTime endDate);
    }
}
