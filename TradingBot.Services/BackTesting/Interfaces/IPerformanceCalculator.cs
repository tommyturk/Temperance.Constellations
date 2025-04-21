using TradingBot.Data.Models.Backtest;

namespace TradingBot.Services.BackTesting.Interfaces
{
    public interface IPerformanceCalculator
    {
        Task CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital);
    }
}
