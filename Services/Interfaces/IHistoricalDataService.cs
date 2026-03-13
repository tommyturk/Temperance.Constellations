using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Constellations.Models.HistoricalData;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IHistoricalDataService
    {
        Task<bool> UpdateHistoricalPrices(List<PriceModel> prices, string symbol, string timeInterval);
    }
}
