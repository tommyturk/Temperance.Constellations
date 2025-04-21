namespace TradingBot.Services.Trading.Strategies.MeanCrossOverStrategy.Interfaces
{
    public interface IMovingAverageCrossoverStrategy
    {
        bool IsBuySignal(List<decimal> priceHistory);
        bool IsSellSignal(List<decimal> priceHistory);
    }
}
