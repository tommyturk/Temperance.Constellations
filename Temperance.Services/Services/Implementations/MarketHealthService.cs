using ILGPU.IR.Types;
using Microsoft.Extensions.Logging;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.MarketHealth;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class MarketHealthService : IMarketHealthService
    {
        private readonly IEconomicDataService _economicDataService;
        private readonly IHistoricalPriceService _historicalPriceService;
        private readonly ILogger<MarketHealthService> _logger;

        public MarketHealthService(IEconomicDataService economicDataService, IHistoricalPriceService historicalPriceService, ILogger<MarketHealthService> logger)
        {
            _economicDataService = economicDataService;
            _historicalPriceService = historicalPriceService;
            _logger = logger;
        }

        public async Task<MarketHealthScore> GetCurrentMarketHealth(DateTime currentDate)
        {
            int totalScore = 0;

            var spyHistory = await _historicalPriceService.GetHistoricalPrices("SPY", "1d", currentDate.AddYears(-2), currentDate);
            if(spyHistory.Count > 200)
            {
                var spy200ma = spyHistory.TakeLast(200).Average(p => p.ClosePrice);
                totalScore += spyHistory.Last().ClosePrice > spy200ma ? 2 : -2;

                var atr20 = CalculateAtr(spyHistory.TakeLast(21).ToList());
                var atr100 = CalculateAtr(spyHistory.TakeLast(101).ToList());
                if (atr100 > 0)
                    totalScore += (atr20 > atr100) ? -1 : 1;
            }

            var yieldSpread = await _economicDataService.GetYieldCurveSpread(currentDate);
            if (yieldSpread < 0) totalScore -= 2;
            else if (yieldSpread < 0.5) totalScore += 0;
            else totalScore += 1;

            var fedRateTrend = await _economicDataService.GetFedRateTrend(currentDate);
            totalScore += (fedRateTrend == Trend.Rising) ? -1 : 1;

            //var cpi = await _economicDataService.GetCpiYoY(currentDate);
            //if (cpi > 0.04) totalScore -= 2;
            //else if (cpi > 0.025) totalScore -= 1;
            //else totalScore += 1;

            var unemploymentTrend = await _economicDataService.GetUnemploymentTrend(currentDate);
            totalScore += (unemploymentTrend == Trend.Rising) ? -2 : 1;

            var gdpGrowth = await _economicDataService.GetRealGdpGrowth(currentDate);
            totalScore += (gdpGrowth < 0) ? -2 : 1;

            return ClassifyScore(totalScore);
        }

        private MarketHealthScore ClassifyScore(int score)
        {
            if (score >= 5) return MarketHealthScore.StronglyBullish;
            if (score >= 1) return MarketHealthScore.Bullish;
            if (score == 0) return MarketHealthScore.Neutral;
            if (score >= -4) return MarketHealthScore.Bearish;
            return MarketHealthScore.StronglyBearish;
        }

        private double CalculateAtr(IReadOnlyList<HistoricalPriceModel> bars)
        {
            if(bars == null || bars.Count < 2)
                return 0;

            var trueRanges = new List<double>();
            for(int i = 1; i < bars.Count; i++)
            {
                var high = bars[i].HighPrice;
                var low = bars[i].LowPrice;
                var prevClose = bars[i - 1].ClosePrice;

                var tr1 = high - low;
                var tr2 = Math.Abs(high - prevClose);
                var tr3 = Math.Abs(low - prevClose);

                trueRanges.Add(Math.Max(tr1, Math.Max(tr2, tr3)));
            }

            return trueRanges.Any() ? trueRanges.Average() : 0;
        }
    }
}
