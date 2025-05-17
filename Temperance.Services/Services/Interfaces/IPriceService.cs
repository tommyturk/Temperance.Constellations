using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Services.Services.Interfaces
{
    public interface IPriceService
    {
        Task<object> CheckSecurityPrices(string symbol, string interval);
    }
}
