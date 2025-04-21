using TradingApp.src.Core.Models.MeanReversion;
using TradingBot.Data.Models.HistoricalPriceData;

namespace TradingApp.src.Core.Strategies.MeanReversion.Interfaces
{
    public interface IMeanReversionStrategy
    {
        Task<List<TradeSignal>> CalculateTrades(List<HistoricalPriceModel> historicalPrices, int windowSize);
    }
}
