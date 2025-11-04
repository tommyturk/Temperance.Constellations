using Temperance.Data.Models.HistoricalPriceData;

namespace TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces
{
    public interface IHistoricalPriceRepository
    {
        Task<decimal> GetLatestPriceAsync(string symbol, DateTime backtestTimestamp);
        Task<List<HistoricalPriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals, DateTime? startDate = null, DateTime? endDate = null);
        Task<List<HistoricalPriceModel>> GetHistoricalPricesForMonth(string symbol, string timeInterval, DateTime startDate, DateTime endDate);
        Task<bool> UpdateHistoricalPrices(List<HistoricalPriceModel> prices, string symbol, string timeInterval);
        Task<List<HistoricalPriceModel>> GetSecurityHistoricalPrices(string symbol, string interval);
        Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval);
        Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval, DateTime startDate, DateTime endDate);
        Task<bool> CheckIfBackfillExists(string symbol, string interval);
        Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval);
        Task<bool> DeleteHistoricalPrices(string symbol, string interval);

    }
}
