using TradingBot.Data.Models.HistoricalData;
using TradingBot.Data.Models.HistoricalPriceData;
using TradingBot.Data.Models.Securities;
using TradingBot.Data.Models.Securities.BalanceSheet;
using TradingBot.Data.Models.Securities.Earnings;
using TradingBot.Data.Models.Securities.SecurityOverview;

namespace TradingBot.Services.Services.Interfaces
{
    public interface IAlphaVantageService
    {
        Task<SecuritySearchResponse> SearchSecurities(string query);

        Task<List<HistoricalPriceModel>> GetIntradayDataBatch(string symbol, string interval, string month);

        Task<List<HistoricalPriceModel>> GetIntradayData(string symbol, string interval);

        //Task<List<HistoricalPriceModel>> GetIntradayDataBatch(string symbol, string interval, string month);

        //Task<List<HistoricalPriceModel>> GetIntradayDataBatch(List<string> symbols, string interval, string month);

        Task<SecuritiesOverview> GetSecuritiesOverviewData(string symbol);
        Task<Earnings> GetSecuritiesEarningsData(string symbol);
        Task<BalanceSheetModel> GetBalanceSheetsData(string symbol);
    }
}
