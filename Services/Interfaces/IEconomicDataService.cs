using Temperance.Constellations.Models.MarketHealth;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IEconomicDataService
    {
        Task<decimal> GetYieldCurveSpread(DateTime asOfDate);
        Task<Trend> GetFedRateTrend(DateTime asOfDate);
        Task<decimal> GetCpiYoY(DateTime asOfDate);
        Task<Trend> GetUnemploymentTrend(DateTime asOfDate);
        Task<decimal> GetRealGdpGrowth(DateTime asOfDate);
    }
}
