using Temperance.Data.Models.MarketHealth;

namespace Temperance.Services.Services.Interfaces
{
    public interface IMarketHealthService
    {
        Task<MarketHealthScore> GetCurrentMarketHealth(DateTime currentDate);
    }
}
