using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingBot.Data.Models.Securities.BalanceSheet;

namespace TradingBot.Services.Services.Interfaces
{
    public interface IBalanceSheetService
    {
        Task<BalanceSheetModel> SecurityBalanceSheet(string symbol);
        Task<bool> UpdateBalanceSheetData(int securityId, string symbol);
    }
}
