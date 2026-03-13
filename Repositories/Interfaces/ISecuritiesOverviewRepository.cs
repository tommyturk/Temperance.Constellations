using Temperance.Ephemeris.Models.Financials;
using Temperance.Constellations.Models;

namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface ISecuritiesOverviewRepository
    {
        Task<List<string>> GetSecurities();

        IAsyncEnumerable<SymbolCoverageBacktestModel> StreamSecuritiesForBacktest(List<string> symbols, List<string> intervals, CancellationToken cancellationToken = default);

        Task<bool> UpdateSecuritiesOverview(int securityId, SecurityOverviewModel SecuritiesOverview);

        Task<SecurityOverviewModel> GetSecurityOverview(string symbol);

        Task<int> GetSecurityId(string symbol);

        Task<bool> DeleteSecurity(string symbol);

        Task<SortedDictionary<DateTime, decimal>> GetSharesOutstandingHistoryAsync(string symbol);

        Task<Dictionary<string, double>> GetSectorAveragePERatiosAsync();
        Task<List<string>> GetUniverseAsOfDateAsync(DateTime asOfDate);
    }
}
