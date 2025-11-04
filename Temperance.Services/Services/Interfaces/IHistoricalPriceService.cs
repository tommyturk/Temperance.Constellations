using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Services.Services.Interfaces
{
    public interface IHistoricalPriceService
    {
        Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval);
        Task<List<HistoricalPriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals, DateTime currentOosStartDate);
        Task<List<HistoricalPriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals, DateTime? startdate = null, DateTime? endDate = null);
        Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval, DateTime startDate, DateTime endDate);
        IEnumerable<BackfillStatus> GetActiveBackfills();
        Task<bool> RunBacktestAsync(string symbol, string interval);
        Task RunBacktestInternalAsync(string symbol, string interval, CancellationToken ct, BackfillStatus status);
        Task<bool> UpdateHistoricalPrices(string symbol, string interval, int year, int month);
        Task<bool> UpdateHistoricalPrices(List<HistoricalPriceModel> latestData, string symbol, string interval);
        Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval);
        Task<List<HistoricalPriceModel>> GetIntradayData(string symbol, string interval, DateTime? lastSavedTimestamp);
        Task<bool> DeleteHistoricalPrices(string symbol, string interval);
    }
}
