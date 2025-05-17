using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Securities;
using Temperance.Data.Models.Securities.BalanceSheet;
using Temperance.Data.Models.Securities.Earnings;
using Temperance.Data.Models.Securities.SecurityOverview;

namespace Temperance.Services.Services.Interfaces
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
