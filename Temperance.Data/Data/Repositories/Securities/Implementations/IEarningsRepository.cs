using Temperance.Data.Models.Securities.Earnings;

namespace Temperance.Data.Data.Repositories.Securities.Implementations
{
    public interface IEarningsRepository
    {
        Task<bool> InsertSecuritiesEarningsData(int securityId, Earnings reports, string symbol);
        Task<bool> InsertAnnualEarnings(int securityId, List<AnnualEarnings> reports, string symbol);
        Task<bool> InsertQuarterlyEarnings(int securityId, List<QuarterlyEarnings> reports, string symbol);
        Task<Earnings> GetSecurityEarningsData(int securityId);
    }
}
