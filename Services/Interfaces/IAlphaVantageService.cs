using Temperance.Ephemeris.Models.Financials;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IAlphaVantageService
    {
        Task<List<PriceModel>> GetIntradayDataBatch(string symbol, string interval, string month);
        Task<List<PriceModel>> GetIntradayData(string symbol, string interval);
        Task<SecurityOverviewModel> GetSecuritiesOverviewData(string symbol);
        Task<EarningsModel> GetSecuritiesEarningsData(string symbol);
        Task<BalanceSheetModel> GetBalanceSheetsData(string symbol);
    }
}
