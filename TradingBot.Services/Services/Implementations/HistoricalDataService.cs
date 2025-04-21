using TradingBot.Data.Data.Repositories.HistoricalData.Interfaces;
using TradingBot.Data.Models.HistoricalData;
using TradingBot.Services.Services.Interfaces;

namespace TradingBot.Services.Services.Implementations
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
