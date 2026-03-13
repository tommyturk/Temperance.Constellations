using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Performance;
using Temperance.Constellations.Models.Trading;

namespace Temperance.Constellations.BackTesting.Interfaces
{
    public interface IPerformanceCalculator
    {
        decimal CalculateProfitLoss(TradeSummary trade);

        Task CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital);

        KellyMetrics CalculateKellyMetrics(IReadOnlyList<TradeSummary> trades);
        Task<List<SleeveComponent>> CalculateSleevePerformanceFromTradesAsync(
                    BacktestResult portfolioSummary,
                    Guid sessionId,
                    Guid portfolioBacktestRunId);
    }
}
