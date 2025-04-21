using TradingBot.Services.Trading.Strategies.ExponentialMovingAverageCrossoverStrategy.Interfaces;

namespace TradingBot.Services.Trading.Strategies.ExponentialMovingAverageCrossoverStrategy.Implementations
{
    public class ExponentialMovingAverageCrossoverStrategy : IExponentialMovingAverageCrossoverStrategy
    {
        private readonly int _shortPeriod = 12;
        private readonly int _longPeriod = 26;
        
        public bool IsBuySignal(List<decimal> priceHistory)
        {
            var shortTermEMA = CalculateExponentialMovingAverage(priceHistory, _shortPeriod);
            var longTermEMA = CalculateExponentialMovingAverage(priceHistory, _longPeriod);
            return shortTermEMA.Last() > longTermEMA.Last() && shortTermEMA[shortTermEMA.Count - 2] < longTermEMA[longTermEMA.Count - 2];
        }

        public bool IsSellSignal(List<decimal> priceHistory)
        {
            var shortTermEMA = CalculateExponentialMovingAverage(priceHistory, _shortPeriod);
            var longTermEMA = CalculateExponentialMovingAverage(priceHistory, _longPeriod);
            return shortTermEMA.Last() < longTermEMA.Last() && shortTermEMA[shortTermEMA.Count - 2] > longTermEMA[longTermEMA.Count - 2];
        }

        private List<decimal> CalculateExponentialMovingAverage(List<decimal> priceHistory, int period)
        {
            var ema = new List<decimal>();
            var multiplier = (decimal)(2.0 / (period + 1));
            var sma = priceHistory.Take(period).Average();
            ema.Add(sma);
            for (int i = period; i < priceHistory.Count; i++)
            {
                var close = priceHistory[i];
                var previousEma = ema.Last();
                var currentEma = (close - previousEma) * multiplier + previousEma;
                ema.Add(currentEma);
            }
            return ema;
        }
    }
}
