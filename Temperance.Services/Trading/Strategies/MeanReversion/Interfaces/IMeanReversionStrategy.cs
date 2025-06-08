using Temperance.Data.Models.Trading;
using Temperance.Data.Models.HistoricalPriceData;

namespace TradingApp.src.Core.Strategies.MeanReversion.Interfaces
{
    public interface IMeanReversionStrategy
    {
        Task<List<TradeSignal>> CalculateTrades(List<HistoricalPriceModel> historicalPrices, int windowSize);
    }
}
