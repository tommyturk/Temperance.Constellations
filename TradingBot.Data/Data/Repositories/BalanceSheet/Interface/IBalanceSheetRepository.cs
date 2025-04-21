using TradingBot.Data.Models.Securities.BalanceSheet;

namespace TradingBot.Data.Data.Repositories.BalanceSheet.Interface
{
    public interface IBalanceSheetRepository
    {
        Task<BalanceSheetModel> GetSecurityBalanceSheet(int securityId);

        Task<bool> InsertSecuritiesBalanceSheetData(int securityId, BalanceSheetModel balanceSheetData, string symbol);
    }
}
