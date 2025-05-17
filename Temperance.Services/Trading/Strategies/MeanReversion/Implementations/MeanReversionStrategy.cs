using TradingApp.src.Core.Models.MeanReversion;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;
using Temperance.Services.Trading.Strategies;

namespace TradingApp.src.Core.Strategies.MeanReversion.Implementations
{
    public class MeanReversionStrategy : ITradingStrategy
    {
        public string Name => "MeanReversion_BB_RSI";

        // --- Strategy Parameters ---
        private int _movingAveragePeriod;
        private decimal _stdDevMultiplier;
        private int _rsiPeriod;
        private decimal _rsiOversoldThreshold;
        private decimal _rsiOverboughtThreshold;
        // ---

        // Define default parameters
        public Dictionary<string, object> GetDefaultParameters() => new()
        {
            { "MovingAveragePeriod", 20 },
            { "StdDevMultiplier", 2.0m },
            { "RSIPeriod", 14 },
            { "RSIOversold", 30m },
            { "RSIOverbought", 70m }
        };

        public void Initialize(decimal initialCapital, Dictionary<string, object> parameters)
        {
            var defaultParams = GetDefaultParameters();
            _movingAveragePeriod = (int)defaultParams["MovingAveragePeriod"];
            _stdDevMultiplier = (decimal)defaultParams["StdDevMultiplier"];
            _rsiPeriod = (int)defaultParams["RSIPeriod"];
            _rsiOversoldThreshold = (decimal)defaultParams["RSIOversold"];
            _rsiOverboughtThreshold = (decimal)defaultParams["RSIOverbought"];

            Console.WriteLine($"Initializing {Name} with MA:{_movingAveragePeriod}, SDMult:{_stdDevMultiplier}, RSI:{_rsiPeriod}, RSI Levels:{_rsiOversoldThreshold}/{_rsiOverboughtThreshold}");
        }

        public int GetRequiredLookbackPeriod()
        {
            return Math.Max(_movingAveragePeriod, _rsiPeriod + 1) + 1;
        }


        public SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow)
        {
            if (historicalDataWindow.Count < _movingAveragePeriod || historicalDataWindow.Count < _rsiPeriod + 1)
                return SignalDecision.Hold;

            var bbWindowPrices = historicalDataWindow.TakeLast(_movingAveragePeriod).Select(h => h.ClosePrice).ToList();
            if (bbWindowPrices.Count < _movingAveragePeriod) return SignalDecision.Hold; // Double check

            decimal simpleMovingAverage = bbWindowPrices.Average();
            decimal standardDeviation = CalculateStdDev(bbWindowPrices);
            decimal upperBollingerBand = simpleMovingAverage + _stdDevMultiplier * standardDeviation;
            decimal lowerBollingerBand = simpleMovingAverage - _stdDevMultiplier * standardDeviation;

            var rsiWindowPrices = historicalDataWindow.Select(h => h.ClosePrice).ToList();
            List<decimal> rsiValues = CalculateRSI(rsiWindowPrices, _rsiPeriod);
            if (rsiValues.Count == 0 || rsiValues.Count < historicalDataWindow.Count)
                return SignalDecision.Hold;

            decimal currentRelativeStrengthIndex = rsiValues.Last();

            if (currentBar.ClosePrice < lowerBollingerBand && currentRelativeStrengthIndex < _rsiOversoldThreshold)
                return SignalDecision.Buy;

            if (currentBar.ClosePrice > upperBollingerBand && currentRelativeStrengthIndex > _rsiOverboughtThreshold)
                return SignalDecision.Sell;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow)
        {
            var currentSignal = GenerateSignal(currentBar, historicalDataWindow);

            if (position.Direction == PositionDirection.Long && currentSignal == SignalDecision.Sell)
                return true;
            if (position.Direction == PositionDirection.Short && currentSignal == SignalDecision.Buy)
                return true;

            decimal stopLossPrice = CalculateStopLoss(position);
            if (position.Direction == PositionDirection.Long && currentBar.LowPrice <= stopLossPrice) return true;
            if (position.Direction == PositionDirection.Short && currentBar.HighPrice >= stopLossPrice) return true;

            decimal takeProfitPrice = CalculateTakeProfit(position);
            if (position.Direction == PositionDirection.Long && currentBar.HighPrice >= takeProfitPrice) return true;
            if (position.Direction == PositionDirection.Short && currentBar.LowPrice <= takeProfitPrice) return true;

            return false;
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            if (activeTrade == null)
            {
                Console.WriteLine("Error: Attempting to close a null trade.");
                return null;
            }

            activeTrade.ExitDate = currentBar.Timestamp;
            activeTrade.ExitPrice = currentBar.ClosePrice;

            decimal profitLoss = 0;

            if (activeTrade.Direction == "Long")
                profitLoss = (activeTrade.ExitPrice.Value - activeTrade.EntryPrice) * activeTrade.Quantity;

            else if (activeTrade.Direction == "Short")
                profitLoss = (activeTrade.EntryPrice - activeTrade.ExitPrice.Value) * activeTrade.Quantity;

            activeTrade.ProfitLoss = profitLoss;

            return activeTrade;
        }

        public decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocation)
        {
            if (historicalDataWindow.Count < _movingAveragePeriod || historicalDataWindow.Count < _rsiPeriod + 1)
                return 0;

            var bbWindowPrices = historicalDataWindow.TakeLast(_movingAveragePeriod).Select(h => h.ClosePrice).ToList();
            if (bbWindowPrices.Count < _movingAveragePeriod) return 0;

            decimal simpleMovingAverage = bbWindowPrices.Average();
            decimal standardDeviation = CalculateStdDev(bbWindowPrices);
            decimal upperBollingerBand = simpleMovingAverage + _stdDevMultiplier * standardDeviation;
            decimal lowerBollingerBand = simpleMovingAverage - _stdDevMultiplier * standardDeviation;

            var rsiWindowPrices = historicalDataWindow.Select(h => h.ClosePrice).ToList();
            List<decimal> rsiValues = CalculateRSI(rsiWindowPrices, _rsiPeriod);
            if (rsiValues.Count == 0 || rsiValues.Count < historicalDataWindow.Count)
                return 0;

            decimal currentRelativeStrengthIndex = rsiValues.Last();

            SignalDecision signal = GenerateSignal(currentBar, historicalDataWindow);

            decimal calculatedAllocation = 0;

            if (signal == SignalDecision.Buy)
            {
                decimal distanceBelowLowerBand = Math.Max(0, lowerBollingerBand - currentBar.ClosePrice);

                decimal distanceBelowRSIOversold = Math.Max(0, _rsiOversoldThreshold - currentRelativeStrengthIndex);

                if (distanceBelowRSIOversold > 0 && currentBar.ClosePrice < lowerBollingerBand)
                {
                    decimal rsiScalingFactor = Math.Min(1.0m, distanceBelowRSIOversold / (_rsiOversoldThreshold - 0m));
                    calculatedAllocation = maxTradeAllocation * rsiScalingFactor;
                }
            }
            else if (signal == SignalDecision.Sell)
            {
                decimal distanceAboveUpperBand = Math.Max(0, currentBar.ClosePrice - upperBollingerBand);

                decimal distanceAboveRSIOverbought = Math.Max(0, currentRelativeStrengthIndex - _rsiOverboughtThreshold);

                if (distanceAboveRSIOverbought > 0 && currentBar.ClosePrice > upperBollingerBand)
                {
                    decimal rsiScalingFactor = Math.Min(1.0m, distanceAboveRSIOverbought / (100m - _rsiOverboughtThreshold));
                    calculatedAllocation = maxTradeAllocation * rsiScalingFactor;
                }
            }

            return Math.Min(calculatedAllocation, maxTradeAllocation);
        }

        private decimal CalculateStdDev(List<decimal> values)
        {
            if (values == null || values.Count <= 1)
                return 0;

            decimal average = values.Average();
            double sumOfSquares = values.Sum(val => Math.Pow((double)(val - average), 2));
            return (decimal)Math.Sqrt(sumOfSquares / (values.Count - 1));
        }

        private List<decimal> CalculateRSI(List<decimal> prices, int period)
        {
            var rsiValues = new List<decimal>();
            if (prices == null || prices.Count <= period)
                return rsiValues;

            decimal gains = 0;
            decimal losses = 0;

            for (int j = 1; j <= period; j++)
            {
                decimal change = prices[j] - prices[j - 1];
                if (change > 0) gains += change;
                else losses += Math.Abs(change);
            }

            decimal avgGain = gains / period;
            decimal avgLoss = losses / period;

            for (int i = period; i < prices.Count; i++)
            {
                gains = 0;
                losses = 0;
                for (int j = i - period + 1; j <= i; j++)
                {
                    decimal change = prices[j] - prices[j - 1];
                    if (change > 0) gains += change;
                    else losses += Math.Abs(change);
                }
                avgGain = gains / period;
                avgLoss = losses / period;

                if (avgLoss == 0)
                    rsiValues.Add(100);
                else
                {
                    decimal relativeStrength = avgGain / avgLoss;
                    decimal rsi = 100 - (100 / (1 + relativeStrength));
                    rsiValues.Add(rsi);
                }
            }

            var padding = Enumerable.Repeat(50m, prices.Count - rsiValues.Count).ToList();
            padding.AddRange(rsiValues);
            return padding;
        }

        protected decimal CalculateStopLoss(Position position)
        {
            decimal stopLossPercentage = 0.05m;

            if (position.Direction == PositionDirection.Long)
                return position.EntryPrice * (1 - stopLossPercentage);
            else
                return position.EntryPrice * (1 + stopLossPercentage);
        }

        protected decimal CalculateTakeProfit(Position position)
        {
            decimal takeProfitPercentage = 0.05m;

            if (position.Direction == PositionDirection.Long)
                return position.EntryPrice * (1 + takeProfitPercentage);
            else
                return position.EntryPrice * (1 - takeProfitPercentage);
        }
    }
}
