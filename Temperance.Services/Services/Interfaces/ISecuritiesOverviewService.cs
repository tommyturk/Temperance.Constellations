using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.Securities.SecurityOverview;

namespace Temperance.Services.Services.Interfaces
{
    public interface ISecuritiesOverviewService
    {
        Task<List<string>> GetSecurities();
        Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbol = null, List<string> intervals = null);
        IAsyncEnumerable<SymbolCoverageBacktestModel> StreamSecuritiesForBacktest(
           List<string> symbols,
           List<string> intervals,
           [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default);
        Task<SecuritiesOverview> GetSecurityOverview(string symbol);
        Task<int> GetSecurityId(string symbol);
        Task<bool> DeleteSecurity(string symbol);

        Task<SortedDictionary<DateTime, decimal>> GetSharesOutstandingHistoryAsync(string symbol);
        Task<Dictionary<string, double>> GetSectorAveragePERatiosAsync();
        Task<List<string>> GetUniverseAsOfDateAsync(DateTime asOfDate);
    }
}
