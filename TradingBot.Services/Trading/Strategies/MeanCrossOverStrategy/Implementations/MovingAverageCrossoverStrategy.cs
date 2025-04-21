using TradingBot.Services.Trading.Strategies.MeanCrossOverStrategy.Interfaces;

namespace TradingBot.Services.Trading.Strategies.MeanCrossOverStrategy.Implementations
{
    public class MovingAverageCrossoverStrategy : IMovingAverageCrossoverStrategy
    {
        private readonly int _shortPeriod = 50;
        private readonly int _longPeriod = 200;

        public bool IsBuySignal(List<decimal> priceHistory)
        {
            var shortTermSMA = CalculateMovingAverage(priceHistory, _shortPeriod);
            var longTermSMA = CalculateMovingAverage(priceHistory, _longPeriod);
            return shortTermSMA.Last() > longTermSMA.Last() && shortTermSMA[shortTermSMA.Count - 2] < longTermSMA[longTermSMA.Count - 2];
        }

        public bool IsSellSignal(List<decimal> priceHistory)
        {
            var shortTermSMA = CalculateMovingAverage(priceHistory, _shortPeriod);
            var longTermSMA = CalculateMovingAverage(priceHistory, _longPeriod);
            return shortTermSMA.Last() < longTermSMA.Last() && shortTermSMA[shortTermSMA.Count - 2] > longTermSMA[longTermSMA.Count - 2];
        }

        private List<decimal> CalculateMovingAverage(List<decimal> priceHistory, int period)
        {
            var sma = new List<decimal>();
            for (int i = 0; i <= priceHistory.Count - period; i++)
            {
                var average = priceHistory.Skip(i).Take(period).Average();
                sma.Add(average);
            }
            return sma;
        }
    }
}
