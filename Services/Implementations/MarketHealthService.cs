using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Prices;

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

        public async Task<MarketRegimeMatrix> GetCurrentMarketHealth(DateTime currentDate)
        {
            int totalScore = 0;
            MarketMomentum microMomentum = MarketMomentum.Neutral;

            // 1. FETCH SPY DATA
            var spyHistory = await _historicalPriceService.GetAllHistoricalPrices(new List<string>() { "SPY" }, new List<string>() { "1d" }, currentDate.AddYears(-2), currentDate);

            if (spyHistory.Count > 200)
            {
                // MACRO: S&P 200-Day Trend
                var spy200ma = spyHistory.TakeLast(200).Average(p => p.ClosePrice);
                totalScore += spyHistory.Last().ClosePrice > spy200ma ? 2 : -2;

                // MACRO: Volatility Expansion
                var atr20 = CalculateAtr(spyHistory.TakeLast(21).ToList());
                var atr100 = CalculateAtr(spyHistory.TakeLast(101).ToList());
                if (atr100 > 0) totalScore += (atr20 > atr100) ? -1 : 1;

                // ==============================================================
                // THE NEW MICRO ENGINE: 5-Day SPY RSI (Broad Market Breadth)
                // ==============================================================
                decimal rsi5 = CalculateSimpleRsi(spyHistory.TakeLast(10).ToList(), 5);

                if (rsi5 <= 25m)
                    microMomentum = MarketMomentum.OversoldBounce; // Extreme Panic = Imminent Bounce
                else if (rsi5 >= 75m)
                    microMomentum = MarketMomentum.Overbought;     // Extreme Greed
                else if (rsi5 <= 40m && spyHistory.Last().ClosePrice < spyHistory[^2].ClosePrice)
                    microMomentum = MarketMomentum.Crashing;       // Standard bleeding
            }

            // 2. MACRO ECONOMIC DATA
            var yieldSpread = await _economicDataService.GetYieldCurveSpread(currentDate);
            if (yieldSpread < 0) totalScore -= 2;
            else if (yieldSpread < 0.5m) totalScore += 0;
            else totalScore += 1;

            var fedRateTrend = await _economicDataService.GetFedRateTrend(currentDate);
            totalScore += (fedRateTrend == Trend.Rising) ? -1 : 1;

            var unemploymentTrend = await _economicDataService.GetUnemploymentTrend(currentDate);
            totalScore += (unemploymentTrend == Trend.Rising) ? -2 : 1;

            var gdpGrowth = await _economicDataService.GetRealGdpGrowth(currentDate);
            totalScore += (gdpGrowth < 0) ? -2 : 1;

            // 3. BUILD THE MATRIX
            return new MarketRegimeMatrix
            {
                OverallRegime = ClassifyMacroScore(totalScore), // Assuming this returns Bearish for < 0
                ShortTermMomentum = microMomentum,
                RawMacroScore = totalScore
            };
        }

        private MarketHealthScore ClassifyMacroScore(int totalScore)
        {
            // ==============================================================
            // LEVEL -2: THE CATASTROPHIC CIRCUIT BREAKER
            // ==============================================================
            // Systemic risk is critical. (e.g., SPY plummeting, VIX > 40, Yield Curve deeply inverted).
            // This is the level that triggers the V3.7 Macro Veto.
            if (totalScore <= -4)
            {
                return MarketHealthScore.StronglyBearish;
            }

            // ==============================================================
            // LEVEL -1: THE STANDARD BEAR MARKET
            // ==============================================================
            // The market is in a confirmed downtrend, but liquidity still exists.
            // This is the "Hunting Ground" for your 4-hour mean reversion snap-backs.
            if (totalScore <= -1)
            {
                return MarketHealthScore.Bearish;
            }

            // ==============================================================
            // LEVEL +2: EUPHORIA / MELT-UP
            // ==============================================================
            // Extreme risk-on environment. (e.g., SPY heavily over 200MA, VIX crushed).
            // Optional: You could use this later to scale down short positions!
            if (totalScore >= 4)
            {
                return MarketHealthScore.StronglyBullish;
            }

            // ==============================================================
            // LEVEL +1: THE STANDARD BULL MARKET
            // ==============================================================
            // The wind is at our backs. GDP growing, stable uptrend.
            if (totalScore >= 1)
            {
                return MarketHealthScore.Bullish;
            }

            // ==============================================================
            // LEVEL 0: THE CHOP
            // ==============================================================
            // The macro data is conflicting. No clear trend direction.
            return MarketHealthScore.Neutral;
        }

        private decimal CalculateSimpleRsi(List<PriceModel> recentPrices, int period)
        {
            if (recentPrices.Count <= period) return 50m;

            decimal gains = 0m;
            decimal losses = 0m;

            for (int i = recentPrices.Count - period; i < recentPrices.Count; i++)
            {
                decimal change = recentPrices[i].ClosePrice - recentPrices[i - 1].ClosePrice;
                if (change > 0) gains += change;
                else losses -= change;
            }

            if (losses == 0) return 100m; // Straight up

            decimal averageGain = gains / period;
            decimal averageLoss = losses / period;
            decimal rs = averageGain / averageLoss;

            return 100m - (100m / (1m + rs));
        }

        private MarketHealthScore ClassifyScore(int score)
        {
            if (score >= 5) return MarketHealthScore.StronglyBullish;
            if (score >= 1) return MarketHealthScore.Bullish;
            if (score == 0) return MarketHealthScore.Neutral;
            if (score >= -4) return MarketHealthScore.Bearish;
            return MarketHealthScore.StronglyBearish;
        }

        private decimal CalculateAtr(IReadOnlyList<PriceModel> bars)
        {
            if(bars == null || bars.Count < 2)
                return 0;

            var trueRanges = new List<decimal>();
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
