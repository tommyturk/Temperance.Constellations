using Microsoft.Extensions.Logging;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class QualityFilterService : IQualityFilterService
    {
        private readonly ILogger<QualityFilterService> _logger;

        // --- Filter Thresholds (Consider making these configurable) ---
        private const double MinProfitMargin = 0.05;  // 5%
        private const double MaxBeta = 1.5;
        private const double MinMarketCap = 5_000_000_000_000; // $10 Billion
        private const double DefaultMaxPERatio = 40.0; // Fallback if sector average is not found
        private const double MinDollarValue = 20_000_000; // $20 Million daily volume
        private const double MaxSpreadPercentage = 0.005; // 0.05% maximum spread

        public QualityFilterService(ILogger<QualityFilterService> logger)
        {
            _logger = logger;
        }
         
        public Task<(bool isHighQuality, string reason)> CheckQualityAsync(string symbol, SecuritiesOverview overviewData,
            Dictionary<string, double> sectorAveragePERatios)
        {
            double dollarVolume = (overviewData.FiftyDayMovingAverage ?? 0) * (overviewData.SharesOutstanding ?? 0);
            if (dollarVolume < MinDollarValue)
                return Task.FromResult((false, $"Fails Liquidity: Dollar Volume ${dollarVolume:N0} < ${MinDollarValue:N0}."));

            if (overviewData == null)
                return Task.FromResult((false, "No SecuritiesOverview data available."));

            if (overviewData.ProfitMargin <= MinProfitMargin)
                return Task.FromResult((false, $"Fails Profitability: Margin {overviewData.ProfitMargin:P2} <= {MinProfitMargin:P2}."));

            double maxPERatio = sectorAveragePERatios.TryGetValue(overviewData.Sector ?? "", out var avgPE)
                ? avgPE
                : DefaultMaxPERatio;

            if (overviewData.PERatio <= 0 || overviewData.PERatio >= maxPERatio)
                return Task.FromResult((false, $"Fails Valuation: P/E {overviewData.PERatio:N2} is not within (0, {maxPERatio:N2})."));

            if (overviewData.Beta >= MaxBeta)
                return Task.FromResult((false, $"Fails Stability: Beta {overviewData.Beta:N2} >= {MaxBeta:N2}."));

            if (overviewData.MarketCapitalization <= MinMarketCap)
                return Task.FromResult((false, $"Fails Size: Market Cap ${overviewData.MarketCapitalization:N0} <= ${MinMarketCap:N0}."));

            return Task.FromResult((true, "Passes all quality filters."));
        }
    }
}
