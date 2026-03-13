
using Temperance.Constellations.Models.MarketHealth;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IMarketHealthService
    {
        Task<MarketHealthScore> GetCurrentMarketHealth(DateTime currentDate);
    }
}
