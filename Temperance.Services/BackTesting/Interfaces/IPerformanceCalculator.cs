using Temperance.Data.Models.Backtest;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPerformanceCalculator
    {
        Task CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital);
    }
}
