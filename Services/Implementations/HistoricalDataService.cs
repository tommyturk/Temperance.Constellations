using Temperance.Constellations.Repositories.Interfaces;
using Temperance.Constellations.Models.HistoricalData;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Constellations.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class HistoricalDataService : IHistoricalDataService
    {
        private readonly IHistoricalDataRepository _historicalDataRepository;

        public HistoricalDataService(IHistoricalDataRepository historicalDataRepository)
        {
            _historicalDataRepository = historicalDataRepository;
        }

        public async Task<bool> UpdateHistoricalPrices(List<PriceModel> prices, string symbol, string timeInterval)
        {
            return await _historicalDataRepository.UpdateHistoricalPrices(prices, symbol, timeInterval);
        }
    }
}
