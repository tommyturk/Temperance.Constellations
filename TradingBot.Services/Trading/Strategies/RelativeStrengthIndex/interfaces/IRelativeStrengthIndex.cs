namespace TradingBot.Services.Trading.Strategies.RelativeStrengthIndex.Interfaces
{
    public interface IRelativeStrengthIndex
    {
        List<decimal> CalculateRelativeStrengthIndex(List<decimal> priceHistory);
    }
}
