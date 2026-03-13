using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IGpuIndicatorService
    {
        Task<Dictionary<string, decimal[]>> CalculateIndicatorsAsync(IReadOnlyList<PriceModel> historicalWindow,
            int strategyMinimumLookback, int atrPeriod, decimal stdDevMultiplier, decimal[] rsi);

        decimal[] CalculateAtr(decimal[] high, decimal[] low, decimal[] close, int period);

        decimal[] CalculateSma(decimal[] prices, int period);

        decimal[] CalculateStdDev(decimal[] prices, int period);

        void Dispose();
    }
}
