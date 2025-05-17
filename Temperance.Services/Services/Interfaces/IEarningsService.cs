using Temperance.Data.Models.Securities.Earnings;
using Temperance.Data.Models.Securities.FinancialReport;

namespace Temperance.Services.Services.Interfaces
{
    public interface IEarningsService
    {
        Task<Earnings> SecurityEarningsData(string query);
        Task<bool> UpdateEarningsData(int securityId, string symbol);
    }
}
