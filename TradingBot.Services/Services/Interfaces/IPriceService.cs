using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingBot.Data.Models.HistoricalPriceData;

namespace TradingBot.Services.Services.Interfaces
{
    public interface IPriceService
    {
        Task<object> CheckSecurityPrices(string symbol, string interval);
    }
}
