using ILGPU;
using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Services.Services.Interfaces
{
    public interface IGpuIndicatorService
    {
        Task<Dictionary<string, double[]>> CalculateIndicatorsAsync(IReadOnlyList<HistoricalPriceModel> historicalWindow,
            int strategyMinimumLookback, int atrPeriod, double stdDevMultiplier, double[] rsi);

        double[] CalculateAtr(double[] high, double[] low, double[] close, int period);

        double[] CalculateSma(double[] prices, int period);

        double[] CalculateStdDev(double[] prices, int period);

        void Dispose();
    }
}
