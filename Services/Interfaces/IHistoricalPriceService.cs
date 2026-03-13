using Temperance.Constellations.Models.HistoricalData;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IHistoricalPriceService
    {
        Task<List<PriceModel>> GetHistoricalPrices(string symbol, string interval);
        Task<List<PriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals, DateTime currentOosStartDate);
        Task<List<PriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals, DateTime? startdate = null, DateTime? endDate = null);
        Task<List<PriceModel>> GetHistoricalPrices(string symbol, string interval, DateTime startDate, DateTime endDate);
        IEnumerable<BackfillStatus> GetActiveBackfills();
        Task<bool> RunBacktestAsync(string symbol, string interval);
        Task RunBacktestInternalAsync(string symbol, string interval, CancellationToken ct, BackfillStatus status);
        Task<bool> UpdateHistoricalPrices(string symbol, string interval, int year, int month);
        Task<bool> UpdateHistoricalPrices(List<PriceModel> latestData, string symbol, string interval);
        Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval);
        Task<List<PriceModel>> GetIntradayData(string symbol, string interval, DateTime? lastSavedTimestamp);
        Task<bool> DeleteHistoricalPrices(string symbol, string interval);
    }
}
