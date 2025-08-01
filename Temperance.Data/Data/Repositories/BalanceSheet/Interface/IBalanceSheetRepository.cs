﻿using Temperance.Data.Models.Securities.BalanceSheet;

namespace Temperance.Data.Data.Repositories.BalanceSheet.Interface
{
    public interface IBalanceSheetRepository
    {
        Task<BalanceSheetModel> GetSecurityBalanceSheet(int securityId);

        Task<bool> InsertSecuritiesBalanceSheetData(int securityId, BalanceSheetModel balanceSheetData, string symbol);
    }
}
