using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.Securities;

namespace Temperance.Data.Data.Repositories.Securities.Interfaces
{
    public interface ISecuritiesOverviewRepository
    {
        Task<List<string>> GetSecurities();

        Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbols = null);

        Task<bool> UpdateSecuritiesOverview(int securityId, SecuritiesOverview SecuritiesOverview);

        Task<SecuritiesOverview> GetSecurityOverview(string symbol);

        Task<int> GetSecurityId(string symbol);

        Task<bool> DeleteSecurity(string symbol);
    }
}
