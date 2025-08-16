using Temperance.Data.Models.MarketHealth;

namespace Temperance.Data.Data.Repositories.Indicator.Interfaces
{
    public interface IIndicatorRepository
    {
        Task<decimal?> GetLatestIndicatorValue(string indicatorTableName, DateTime asOfDate);
        Task<List<IndicatorValue>> GetRecentIndicatorValues(string indicatorTableName, DateTime asOfDate, int count);
        Task<decimal?> GetTreasuryYieldOnDate(string maturity, DateTime asOfDate);
    }
}
