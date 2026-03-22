using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Services.Services.Implementations
{
    public class LiquidityService : ILiquidityService
    {
        private readonly ILogger<LiquidityService> _logger;
        private readonly IHistoricalPriceService _historicalPriceService;

        public LiquidityService(ILogger<LiquidityService> logger, IHistoricalPriceService historicalPriceService)
        {
            _logger = logger;
            _historicalPriceService = historicalPriceService;
        }

        public bool IsSymbolLiquidAtTime(string symbol, string interval, long minAverageVolume, DateTime currentTimestamp, int rollingLookbackBars,
            ReadOnlySpan<PriceModel> fullHistoricalData)
        {
            if (fullHistoricalData.Length < rollingLookbackBars)
                return false;

            var relevantData = fullHistoricalData.Slice(fullHistoricalData.Length - rollingLookbackBars);

            long totalVolume = 0;
            foreach (var bar in relevantData)
                totalVolume += bar.Volume;

            double averageVolume = (double)totalVolume / relevantData.Length;

            return averageVolume < minAverageVolume;
        }

        public bool IsSymbolLiquidAtTime(string symbol, string interval, long minAverageVolume, DateTime currentTimestamp, int rollingLookbackBars,
            IReadOnlyList<PriceModel> fullHistoricalData)
        {
            var relevantData = fullHistoricalData
                .Where(x => x.Timestamp < currentTimestamp)
                .OrderByDescending(x => x.Timestamp)
                .Take(rollingLookbackBars)
                .ToList();

            if (relevantData.Count < rollingLookbackBars * 0.8)
                return false;

            long totalVolume = relevantData.Sum(x => x.Volume);
            if (relevantData.Count == 0) return false;

            double averageVolume = (double)totalVolume / relevantData.Count;
            return averageVolume >= minAverageVolume;
        }

        //public async Task<bool> ISymbolLiquidForPeriod(string symbol, string interval, long minADV, DateTime startDate, DateTime endDate)
        //{
        //    try
        //    {
        //        var historicalPrices = await _historicalPriceService.GetAllHistoricalPrices(symbol, interval);
        //        if (historicalPrices == null || !historicalPrices.Any())
        //        {
        //            _logger.LogWarning("No historical prices found for symbol {Symbol} during the period {StartDate} to {EndDate}.", symbol);
        //            return false;
        //        }

        //        long totalVolumeInPeriod = historicalPrices.Sum(x => x.Volume);
        //        var totalDaysInPeriod = historicalPrices.Select(x => x.Timestamp).Distinct().Count();
        //        if (totalDaysInPeriod == 0)
        //        {
        //            _logger.LogWarning("No trading days found for symbol {Symbol} during the period {StartDate} to {EndDate}.", symbol);
        //            return false;
        //        }

        //        double averageDailyVolume = (double)totalVolumeInPeriod / totalDaysInPeriod;
        //        bool isLiquid = averageDailyVolume >= minADV;

        //        return isLiquid;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error checking liquidity for symbol {Symbol} during the period {StartDate} to {EndDate}.", symbol);
        //        return false;
        //    }
        //}
    }
}
