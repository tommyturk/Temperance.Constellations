namespace TradingBot.Services.Trading.Strategies.ExponentialMovingAverageCrossoverStrategy.Interfaces
{
    public interface IExponentialMovingAverageCrossoverStrategy
    {
        bool IsBuySignal(List<decimal> priceHistory);
        bool IsSellSignal(List<decimal> priceHistory);
    }
}
