using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPerformanceCalculator
    {
        Task CalculatePerformanceMetrics(BacktestResult result, double initialCapital);

        KellyMetrics CalculateKellyMetrics(IReadOnlyList<TradeSummary> trades);
    }
}
