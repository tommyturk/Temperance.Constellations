using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Services.Services.Interfaces
{
    public interface ILiquidityService
    {
        bool IsSymbolLiquidAtTime(string symbol, string interval, long minAverageVolume, DateTime currentTimestamp, int rollingLookbackBars,
            IReadOnlyList<HistoricalPriceModel> fullHistoricalData);
        Task<bool> ISymbolLiquidForPeriod(string symbol, string interval, long minADV, DateTime startDate, DateTime endDate);
    }
}
