using TradingBot.Data.Models.HistoricalData;

namespace TradingBot.Services.Services.Interfaces
{
    public interface IMarketDataService
    {
        Task<List<HistoricalData>> GetMarketData(string symbol, string interval);
        Task<List<HistoricalData>> GetMultipleSecurityMarketData(List<string> symbols, string timeInterval);
    }
}
