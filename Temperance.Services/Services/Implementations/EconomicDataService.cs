using Microsoft.Extensions.Logging;
using Temperance.Data.Data.Repositories.Indicator.Interfaces;
using Temperance.Data.Models.MarketHealth;
using Temperance.Services.Services.Interfaces;

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

        public async Task<double> GetCpiYoY(DateTime asOfDate)
        {
            throw new NotImplementedException();
        }

        public async Task<Trend> GetFedRateTrend(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValues("Federal_Funds_Rate", asOfDate, 2);
            if (recentValues.Count < 2) return Trend.Neutral;

            if (recentValues[1].Value > recentValues[0].Value) return Trend.Rising;
            if (recentValues[1].Value < recentValues[0].Value) return Trend.Falling;

            return Trend.Neutral;
        }

        public async Task<double> GetRealGdpGrowth(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValues("Real_GDP", asOfDate, 2);
            if (recentValues.Count < 2)
                return 0.0;

            var latestGdp = recentValues[1].Value;
            var previousGdp = recentValues[0].Value;
            if (previousGdp == 0) return 0.0;

            return (double)(((latestGdp - previousGdp) / previousGdp) * 100);
        }

        public async Task<Trend> GetUnemploymentTrend(DateTime asOfDate)
        {
            var recentValues = await _indicatorRepository.GetRecentIndicatorValues("Unemployment_Rate", asOfDate, 3);
            if (recentValues.Count < 2) return Trend.Neutral;

            if (recentValues.Last().Value > recentValues.First().Value) return Trend.Rising;
            if (recentValues.Last().Value < recentValues.First().Value) return Trend.Falling;

            return Trend.Neutral;
        }

        public async Task<double> GetYieldCurveSpread(DateTime asOfDate)
        {
            var yield10y = await _indicatorRepository.GetTreasuryYieldOnDate("10year", asOfDate);
            var yield2y = await _indicatorRepository.GetTreasuryYieldOnDate("2year", asOfDate);

            if (yield10y.HasValue && yield2y.HasValue)
                return (double)(yield10y.Value - yield2y.Value);

            return 0.5; // return neutral 
        }
    }
}
