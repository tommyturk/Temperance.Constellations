using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.Securities.SecurityOverview;

namespace Temperance.Services.Services.Interfaces
{
    public interface ISecuritiesOverviewService
    {
        Task<List<string>> GetSecurities();
        Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbol = null, List<string> intervals = null);
        Task<bool> UpdateSecuritiesOverviewData(int securityId, string symbols);
        Task<SecuritySearchResponse> SearchSecuritiesAsync(string query);
        Task<SecuritiesOverview> GetSecurityOverview(string symbol);
        Task<int> GetSecurityId(string symbol);
        Task<bool> DeleteSecurity(string symbol);
    }
}
