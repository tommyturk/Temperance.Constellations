using TradingBot.Data.Models.Securities;

namespace TradingBot.Data.Data.Repositories.Securities.Interfaces
{
    public interface ISecuritiesOverviewRepository
    {
        Task<List<string>> GetSecurities();

        Task<bool> UpdateSecuritiesOverview(int securityId, SecuritiesOverview SecuritiesOverview);

        Task<SecuritiesOverview> GetSecurityOverview(string symbol);

        Task<int> GetSecurityId(string symbol);
        Task<bool> DeleteSecurity(string symbol);
    }
}
