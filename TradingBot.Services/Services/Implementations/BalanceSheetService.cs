using TradingBot.Data.Data.Repositories.BalanceSheet.Interface;
using TradingBot.Data.Models.Securities.BalanceSheet;
using TradingBot.Services.Services.Interfaces;

namespace TradingBot.Services.Services.Implementations
{
    public class BalanceSheetService : IBalanceSheetService
    {
        private readonly IAlphaVantageService _alphaVantageService;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IBalanceSheetRepository _balanceSheetRepository;
        public BalanceSheetService(IAlphaVantageService alphaVantageService, ISecuritiesOverviewService securitiesOverviewService, IBalanceSheetRepository balanceSheetService)
        {
            _alphaVantageService = alphaVantageService;
            _securitiesOverviewService = securitiesOverviewService;
            _balanceSheetRepository = balanceSheetService;
        }

        public async Task<BalanceSheetModel> SecurityBalanceSheet(string symbol)
        {
            var securityId = await _securitiesOverviewService.GetSecurityId(symbol);

            BalanceSheetModel balanceSheet = await _balanceSheetRepository.GetSecurityBalanceSheet(securityId);
            if (balanceSheet.QuarterlyReports.Any() || balanceSheet.AnnualReports.Any())
                return balanceSheet;

            var getBalanceSheet = await UpdateBalanceSheetData(securityId, symbol);
            if (getBalanceSheet == null)
                throw new Exception($"Cannot get balance sheet data for {symbol}");

            return await _balanceSheetRepository.GetSecurityBalanceSheet(securityId);
        }

        public async Task<bool> UpdateBalanceSheetData(int securityId, string symbol)
        {
            var balanceSheetData = await _alphaVantageService.GetBalanceSheetsData(symbol);
            if (balanceSheetData == null) throw new Exception($"Failed to fetch balance sheet data for {symbol}");

            Console.WriteLine($"Fetched earnings data for {symbol}");

            var updateBalanceSheetDataSuccess = await _balanceSheetRepository.InsertSecuritiesBalanceSheetData(securityId,balanceSheetData, symbol);
            if (!updateBalanceSheetDataSuccess) Console.WriteLine($"Failed to insert balance sheet data");

            Console.WriteLine($"Successfully added balance sheet data for {symbol}");
            return true;
        }
    }
}
