using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Repositories.Financials.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class EconomicDataService : IEconomicDataService
    {
        private readonly ILogger<EconomicDataService> _logger;
        private readonly IIndicatorRepository _indicatorRepository;

        public EconomicDataService(ILogger<EconomicDataService> logger, IIndicatorRepository indicatorRepository)
        {
            _logger = logger;
            _indicatorRepository = indicatorRepository;
        }

        public async Task<decimal> GetCpiYoY(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValuesAsync("Inflation", asOfDate, 1);

            if (recentValues == null || !recentValues.Any())
            {
                _logger.LogWarning("Missing CPI (Inflation) data as of {AsOfDate}. Defaulting to 0.0.", asOfDate);
                return 0.0m;
            }

            return recentValues.First().Value;
        }

        public async Task<Trend> GetFedRateTrend(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValuesAsync("Federal_Funds_Rate", asOfDate, 2);
            if (recentValues == null || recentValues.Count < 2) return Trend.Neutral;

            var newest = recentValues[0].Value;
            var previous = recentValues[1].Value;

            if (newest > previous) return Trend.Rising;
            if (newest < previous) return Trend.Falling;

            return Trend.Neutral;
        }

        public async Task<decimal> GetRealGdpGrowth(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValuesAsync("Real_GDP", asOfDate, 2);
            if (recentValues == null || recentValues.Count < 2)
                return 0.0m;

            var latestGdp = recentValues[0].Value;
            var previousGdp = recentValues[1].Value;

            if (previousGdp == 0) return 0.0m; // Prevent division by zero mathematically

            return ((latestGdp - previousGdp) / previousGdp) * 100;
        }

        public async Task<Trend> GetUnemploymentTrend(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValuesAsync("Unemployment_Rate", asOfDate, 3);
            if (recentValues == null || recentValues.Count < 2) return Trend.Neutral;

            // Trend analysis: compare the most recent value to the oldest in our lookback window
            var newest = recentValues.First().Value;
            var oldest = recentValues.Last().Value;

            if (newest > oldest) return Trend.Rising;
            if (newest < oldest) return Trend.Falling;

            return Trend.Neutral;
        }

        public async Task<decimal> GetYieldCurveSpread(DateTime asOfDate)
        {
            var yield10y = await _indicatorRepository.GetTreasuryYieldOnDateAsync("10year", asOfDate);
            var yield2y = await _indicatorRepository.GetTreasuryYieldOnDateAsync("2year", asOfDate);

            if (yield10y.HasValue && yield2y.HasValue)
            {
                return (decimal)(yield10y.Value - yield2y.Value);
            }

            _logger.LogWarning("Incomplete Treasury Yield data for spread calculation as of {AsOfDate}. Returning neutral 0.5 spread.", asOfDate);
            return 0.5m;
        }
    }
}