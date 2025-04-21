using TradingBot.Data.Models.Securities.Earnings;
using TradingBot.Data.Models.Securities.FinancialReport;

namespace TradingBot.Services.Services.Interfaces
{
    public interface IEarningsService
    {
        Task<Earnings> SecurityEarningsData(string query);
        Task<bool> UpdateEarningsData(int securityId, string symbol);
    }
}
