using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPerformanceCalculator
    {
        double CalculateProfitLoss(TradeSummary trade);

        Task CalculatePerformanceMetrics(BacktestResult result, double initialCapital);

        KellyMetrics CalculateKellyMetrics(IReadOnlyList<TradeSummary> trades);
    }
}
