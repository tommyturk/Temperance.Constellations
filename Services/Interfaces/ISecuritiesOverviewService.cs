using Temperance.Ephemeris.Models.Financials;
using Temperance.Constellations.Models;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface ISecuritiesOverviewService
    {
        Task<List<string>> GetSecurities();
        IAsyncEnumerable<SymbolCoverageBacktestModel> StreamSecuritiesForBacktest(
           List<string> symbols,
           List<string> intervals,
           [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default);
        Task<SecurityOverviewModel> GetSecurityOverview(string symbol);
        Task<int> GetSecurityId(string symbol);
        Task<bool> DeleteSecurity(string symbol);

        Task<SortedDictionary<DateTime, decimal>> GetSharesOutstandingHistoryAsync(string symbol);
        Task<Dictionary<string, double>> GetSectorAveragePERatiosAsync();
        Task<List<string>> GetUniverseAsOfDateAsync(DateTime asOfDate);
    }
}
