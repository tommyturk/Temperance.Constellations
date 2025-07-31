using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.Securities;

namespace Temperance.Data.Data.Repositories.Securities.Interfaces
{
    public interface ISecuritiesOverviewRepository
    {
        Task<List<string>> GetSecurities();

        IAsyncEnumerable<SymbolCoverageBacktestModel> StreamSecuritiesForBacktest(List<string> symbols, List<string> intervals, CancellationToken cancellationToken = default);

        Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbols = null, List<string> intervals = null);

        Task<bool> UpdateSecuritiesOverview(int securityId, SecuritiesOverview SecuritiesOverview);

        Task<SecuritiesOverview> GetSecurityOverview(string symbol);

        Task<int> GetSecurityId(string symbol);

        Task<bool> DeleteSecurity(string symbol);
    }
}
