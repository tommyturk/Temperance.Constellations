using Temperance.Data.Models.MarketHealth;

namespace Temperance.Services.Services.Interfaces
{
    public interface IEconomicDataService
    {
        Task<double> GetYieldCurveSpread(DateTime asOfDate);
        Task<Trend> GetFedRateTrend(DateTime asOfDate);
        Task<double> GetCpiYoY(DateTime asOfDate);
        Task<Trend> GetUnemploymentTrend(DateTime asOfDate);
        Task<double> GetRealGdpGrowth(DateTime asOfDate);
    }
}
