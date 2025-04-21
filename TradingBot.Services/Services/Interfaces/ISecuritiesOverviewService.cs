using TradingBot.Data.Models.Securities.SecurityOverview;

namespace TradingBot.Services.Services.Interfaces
{
    public interface ISecuritiesOverviewService
    {
        Task<List<string>> GetSecurities();
        Task<bool> UpdateSecuritiesOverviewData(int securityId, string symbols);
        Task<SecuritySearchResponse> SearchSecuritiesAsync(string query);
        Task<SecuritiesOverview> GetSecurityOverview(string symbol);
        Task<int> GetSecurityId(string symbol);
        Task<bool> DeleteSecurity(string symbol);
    }
}
