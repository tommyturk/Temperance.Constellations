using Temperance.Constellations.Models.HistoricalPriceData;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface ILiquidityService
    {
        bool IsSymbolLiquidAtTime(string symbol, string interval, long minAverageVolume, DateTime currentTimestamp, int rollingLookbackBars,
            ReadOnlySpan<PriceModel> fullHistoricalData);
        bool IsSymbolLiquidAtTime(string symbol, string interval, long minAverageVolume, DateTime currentTimestamp, int rollingLookbackBars,
            IReadOnlyList<PriceModel> fullHistoricalData);
        //Task<bool> ISymbolLiquidForPeriod(string symbol, string interval, long minADV, DateTime startDate, DateTime endDate);
    }
}
