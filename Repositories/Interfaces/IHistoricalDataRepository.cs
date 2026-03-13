using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface IHistoricalDataRepository
    {
        Task<bool> UpdateHistoricalPrices(List<PriceModel> prices, string symbol, string timeInterval);
    }
}
