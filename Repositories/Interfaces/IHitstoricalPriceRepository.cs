using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface IHistoricalPriceRepository
    {
        Task<decimal> GetLatestPriceAsync(string symbol, DateTime backtestTimestamp);
        Task<List<PriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals, DateTime? startDate = null, DateTime? endDate = null);
        Task<List<PriceModel>> GetHistoricalPricesForMonth(string symbol, string timeInterval, DateTime startDate, DateTime endDate);
        Task<bool> UpdateHistoricalPrices(List<PriceModel> prices, string symbol, string timeInterval);
        Task<List<PriceModel>> GetSecurityHistoricalPrices(string symbol, string interval);
        Task<List<PriceModel>> GetHistoricalPrices(string symbol, string interval);
        Task<List<PriceModel>> GetHistoricalPrices(string symbol, string interval, DateTime startDate, DateTime endDate);
        Task<bool> CheckIfBackfillExists(string symbol, string interval);
        Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval);
        Task<bool> DeleteHistoricalPrices(string symbol, string interval);

    }
}
