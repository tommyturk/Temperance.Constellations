using TradingBot.Services.Trading.Strategies.RelativeStrengthIndex.Interfaces;

namespace TradingBot.Services.Trading.Strategies.RelativeStrengthIndex.Implementations
{
    public class RelativeStrengthIndex : IRelativeStrengthIndex
    {
        private readonly int _period = 14;

        public List<decimal> CalculateRelativeStrengthIndex(List<decimal> priceHistory)
        {
            var rsiValues = new List<decimal>();

            if (priceHistory.Count < _period)
                return rsiValues;

            var priceChanges = new List<decimal>();
            for (int i = 1; i < priceHistory.Count; i++)
                priceChanges.Add(priceHistory[i] - priceHistory[i - 1]);

            var gains = new List<decimal>();
            var losses = new List<decimal>();
            for (int i = 0; i < priceChanges.Count; i++)
            {
                if (priceChanges[i] > 0)
                    gains.Add(priceChanges[i]);
                else
                    losses.Add(Math.Abs(priceChanges[i]));
            }

            var averageGain = gains.Take(_period).DefaultIfEmpty(0).Average();
            var averageLoss = losses.Take(_period).DefaultIfEmpty(0).Average();

            decimal relativeStrength = averageGain / averageLoss;
            decimal relativeStrengthIndex = 100 - (100 / (1 + relativeStrength));

            for (int i = _period; i < priceChanges.Count; i++)
            {
                decimal gain = priceChanges[i] > 0 ? priceChanges[i] : 0;
                decimal loss = priceChanges[i] < 0 ? Math.Abs(priceChanges[i]) : 0;

                averageGain = ((averageGain * (_period - 1)) + gain) / _period;
                averageLoss = ((averageLoss * (_period - 1)) + loss) / _period;

                relativeStrength = averageGain / averageLoss;
                relativeStrengthIndex = 100 - (100 / (1 + relativeStrength));

                rsiValues.Add(relativeStrengthIndex);
            }

            return rsiValues;
        }
    }
}
