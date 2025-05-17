using TradingApp.src.Core.Services.Implementations;
using TradingApp.src.Data.Repositories.HistoricalPrices.Implementations;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class PriceService : IPriceService
    {
        private readonly IHistoricalPriceRepository _historicalPriceRepository;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        public PriceService(IHistoricalPriceRepository historicalPriceRepository, ISecuritiesOverviewService securitiesOverviewService)
        {
            _historicalPriceRepository = historicalPriceRepository;
            _securitiesOverviewService = securitiesOverviewService;
        }

        public async Task<object> CheckSecurityPrices(string symbol, string interval)
        {
            bool checkIfBackfillExists = await _historicalPriceRepository.CheckIfBackfillExists(symbol, interval);
            if (!checkIfBackfillExists)
                return false;
            var securityId = await _securitiesOverviewService.GetSecurityId(symbol);
            List<HistoricalPriceModel> prices = await _historicalPriceRepository.GetSecurityHistoricalPrices(symbol, interval);
            if (prices.Any())
                return prices;

            return false;
        }
    }
}
