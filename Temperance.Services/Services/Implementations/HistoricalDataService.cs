using Temperance.Data.Data.Repositories.HistoricalData.Interfaces;
using Temperance.Data.Models.HistoricalData;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class HistoricalDataService : IHistoricalDataService
    {
        private readonly IHistoricalDataRepository _historicalDataRepository;

        public HistoricalDataService(IHistoricalDataRepository historicalDataRepository)
        {
            _historicalDataRepository = historicalDataRepository;
        }

        public async Task<bool> UpdateHistoricalPrices(List<HistoricalData> prices, string symbol, string timeInterval)
        {
            return await _historicalDataRepository.UpdateHistoricalPrices(prices, symbol, timeInterval);
        }
    }
}
