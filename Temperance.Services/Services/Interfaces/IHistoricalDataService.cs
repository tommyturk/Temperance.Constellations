using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.HistoricalData;

namespace Temperance.Services.Services.Interfaces
{
    public interface IHistoricalDataService
    {
        Task<bool> UpdateHistoricalPrices(List<HistoricalData> prices, string symbol, string timeInterval);
    }
}
