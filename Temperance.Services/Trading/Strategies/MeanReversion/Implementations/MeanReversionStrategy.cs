using Microsoft.Extensions.Logging;
using System.Text.Json;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;
using Temperance.Utilities.Helpers;

namespace Temperance.Services.Trading.Strategies.MeanReversion.Implementation
{
    public class MeanReversionStrategy : ISingleAssetStrategy
    {
        public string Name => "MeanReversion_BB_RSI";

        // --- Strategy Parameters ---
        private int _movingAveragePeriod;
        private double _stdDevMultiplier;
        private int _rsiPeriod;
        private double _rsiOversoldThreshold;
        private double _rsiOverboughtThreshold;
        private double _minimumAverageDailyVolume;
        // ---

        private readonly ITransactionCostService _transactionCostService;
        private readonly ILogger<MeanReversionStrategy> _logger;
        public MeanReversionStrategy(ITransactionCostService transactionCostService, ILogger<MeanReversionStrategy> logger)
        {
            _transactionCostService = transactionCostService;
            _logger = logger;
            _logger.LogDebug("MeanReversionStrategy instance created via DI.");
        }


        // Define default parameters
        public Dictionary<string, object> GetDefaultParameters() => new()
        {
            { "MovingAveragePeriod", 20 },
            { "StdDevMultiplier", 2.0m },
            { "RSIPeriod", 14 },
            { "RSIOversold", 30m },
            { "RSIOverbought", 70m },
            { "MinimumAverageDailyVolume", 750000m }
        };

        public void Initialize(double initialCapital, Dictionary<string, object> parameters)
        {
            _movingAveragePeriod = ParameterHelper.GetParameterOrDefault(parameters, "MovingAveragePeriod", 20);
            _stdDevMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "StdDevMultiplier", 2.0);
            _rsiPeriod = ParameterHelper.GetParameterOrDefault(parameters, "RSIPeriod", 14);
            _rsiOversoldThreshold = ParameterHelper.GetParameterOrDefault(parameters, "RSIOversold", 30);
            _rsiOverboughtThreshold = ParameterHelper.GetParameterOrDefault(parameters, "RSIOverbought", 70);
            _minimumAverageDailyVolume = ParameterHelper.GetParameterOrDefault(parameters, "MinimumAverageDailyVolume", 750000); // Ensure 750000m is decimal literal

            _logger.LogInformation($"Initializing {Name} with MA:{_movingAveragePeriod}, SDMult:{_stdDevMultiplier}, RSI:{_rsiPeriod}, RSI Levels:{_rsiOversoldThreshold}/{_rsiOverboughtThreshold}");
        }

        public int GetRequiredLookbackPeriod()
        {
            return Math.Max(_movingAveragePeriod, _rsiPeriod + 1) + 1;
        }

        public long GetMinimumAverageDailyVolume()
        {
            return (long)_minimumAverageDailyVolume;
        }

        public SignalDecision GenerateSignal(in HistoricalPriceModel currentBar, Position currentPosition, ReadOnlySpan<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues)
        {
            int totalBars = historicalDataWindow.Length;
            if (totalBars < GetRequiredLookbackPeriod())
                return SignalDecision.Hold;

            // --- On-the-fly Bollinger Bands Calculation ---
            var bbWindow = historicalDataWindow.Slice(totalBars - _movingAveragePeriod);
            double simpleMovingAverage = 0;
            foreach (var bar in bbWindow) { simpleMovingAverage += bar.ClosePrice; }
            simpleMovingAverage /= _movingAveragePeriod;

            double stdDev = CalculateStdDev(bbWindow, simpleMovingAverage);
            double lowerBollingerBand = simpleMovingAverage - (_stdDevMultiplier * stdDev);
            double upperBollingerBand = simpleMovingAverage + (_stdDevMultiplier * stdDev);

            // --- On-the-fly RSI Calculation ---
            var rsiWindow = historicalDataWindow.Slice(totalBars - (_rsiPeriod + 1));
            double currentRsi = currentIndicatorValues["RSI"];

            // --- Signal Logic ---
            if (currentBar.ClosePrice < lowerBollingerBand && currentRsi < _rsiOversoldThreshold)
                return SignalDecision.Buy;

            if (currentBar.ClosePrice > upperBollingerBand && currentRsi > _rsiOverboughtThreshold)
                return SignalDecision.Sell;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(
            Position position,
            in HistoricalPriceModel currentBar,
            ReadOnlySpan<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            // --- 1. Primary Exit: Reversion to the Mean ---
            // We need to calculate the simple moving average for the current bar.
            var smaWindow = historicalDataWindow.Slice(historicalDataWindow.Length - _movingAveragePeriod);
            double simpleMovingAverage = 0;
            foreach (var bar in smaWindow)
                simpleMovingAverage += bar.ClosePrice;
            
            simpleMovingAverage /= _movingAveragePeriod;

            if (position.Direction == PositionDirection.Long && currentBar.ClosePrice >= simpleMovingAverage)
                return true;

            if (position.Direction == PositionDirection.Short && currentBar.ClosePrice <= simpleMovingAverage)
                return true;

            var currentSignal = GenerateSignal(in currentBar, position, historicalDataWindow, currentIndicatorValues);
            if (position.Direction == PositionDirection.Long && currentSignal == SignalDecision.Sell) return true;
            if (position.Direction == PositionDirection.Short && currentSignal == SignalDecision.Buy) return true;

            return false;
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            if (activeTrade == null) return null; // Or throw

            double rawExitPrice = currentBar.ClosePrice;

            double effectiveExitPrice = _transactionCostService.CalculateExitCost(rawExitPrice, activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short);

            activeTrade.ExitDate = currentBar.Timestamp;
            activeTrade.ExitPrice = effectiveExitPrice;

            double profitLoss = 0;
            if (activeTrade.Direction == "Long")
                profitLoss = (activeTrade.ExitPrice.Value - activeTrade.EntryPrice) * activeTrade.Quantity; // Use effective entry/exit
            else if (activeTrade.Direction == "Short")
                profitLoss = (activeTrade.EntryPrice - activeTrade.ExitPrice.Value) * activeTrade.Quantity;

            activeTrade.ProfitLoss = profitLoss;
            activeTrade.TransactionCost = _transactionCostService.CalculateTotalCost(activeTrade.EntryPrice, rawExitPrice, (activeTrade.Direction == "Long" ? SignalDecision.Buy : SignalDecision.Sell), (activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short), activeTrade.Quantity);

            return activeTrade;
        }

        public double GetAllocationAmount(
            in HistoricalPriceModel currentBar,
            ReadOnlySpan<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues,
            double maxTradeAllocationInitialCapital,
            double currentTotalEquity,
            double kellyHalfFraction)
        {
            if (currentIndicatorValues == null || !currentIndicatorValues.ContainsKey("RSI"))
            {
                _logger.LogWarning("GetAllocationAmount called without required RSI indicator values.");
                return 0;
            }

            double lowerBollingerBand = currentIndicatorValues["LowerBand"];
            double upperBollingerBand = currentIndicatorValues["UpperBand"];
            double currentRelativeStrengthIndex = currentIndicatorValues["RSI"];

            double calculatedAllocationFromStrategyLogic = 0;

            if (currentBar.ClosePrice < lowerBollingerBand && currentRelativeStrengthIndex < _rsiOversoldThreshold)
            {
                double distanceBelowRSIOversold = Math.Max(0, _rsiOversoldThreshold - currentRelativeStrengthIndex);
                double rsiScalingFactor = Math.Min(1.0, distanceBelowRSIOversold / _rsiOversoldThreshold);
                calculatedAllocationFromStrategyLogic = maxTradeAllocationInitialCapital * rsiScalingFactor;
            }
            else if (currentBar.ClosePrice > upperBollingerBand && currentRelativeStrengthIndex > _rsiOverboughtThreshold)
            {
                double distanceAboveRSIOverbought = Math.Max(0, currentRelativeStrengthIndex - _rsiOverboughtThreshold);
                double rsiScalingFactor = Math.Min(1.0, distanceAboveRSIOverbought / (100.0 - _rsiOverboughtThreshold));
                calculatedAllocationFromStrategyLogic = maxTradeAllocationInitialCapital * rsiScalingFactor;
            }

            if (calculatedAllocationFromStrategyLogic <= 0)
                return 0;

            double kellySizedAllocation = currentTotalEquity * kellyHalfFraction;
            if (kellySizedAllocation < 0) kellySizedAllocation = 0;

            double finalAllocationAmount = Math.Min(
                calculatedAllocationFromStrategyLogic,
                Math.Min(maxTradeAllocationInitialCapital, kellySizedAllocation)
            );

            return finalAllocationAmount > 0 ? finalAllocationAmount : 0;
        }

        private double CalculateStdDev(ReadOnlySpan<HistoricalPriceModel> values, double average)
        {
            if (values.Length <= 1) return 0;
            double sumOfSquares = 0;
            foreach (var val in values)
            {
                sumOfSquares += Math.Pow(val.ClosePrice - average, 2);
            }
            return Math.Sqrt(sumOfSquares / (values.Length - 1));
        }

        protected double CalculateStopLoss(Position position)
        {
            double stopLossPercentage = 0.05;

            if (position.Direction == PositionDirection.Long)
                return position.EntryPrice * (1 - stopLossPercentage);
            else
                return position.EntryPrice * (1 + stopLossPercentage);
        }

        protected double CalculateTakeProfit(Position position)
        {
            double takeProfitPercentage = 0.05;

            if (position.Direction == PositionDirection.Long)
                return position.EntryPrice * (1 + takeProfitPercentage);
            else
                return position.EntryPrice * (1 - takeProfitPercentage);
        }

        public double GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, double maxTradeAllocation)
        {
            throw new NotImplementedException();
        }

        public double[] CalculateRSI(double[] prices, int period)
        {
            if (prices == null || prices.Length <= period)
            {
                var neutralRsi = new double[prices?.Length ?? 0];
                Array.Fill(neutralRsi, 50);
                return neutralRsi;
            }

            var rsiValues = new double[prices.Length];
            double initialGainSum = 0;
            double initialLossSum = 0;

            for (int i = 1; i <= period; i++)
            {
                double change = prices[i] - prices[i - 1];
                if (change > 0)
                {
                    initialGainSum += change;
                }
                else
                {
                    initialLossSum -= change;
                }
            }

            double avgGain = initialGainSum / period;
            double avgLoss = initialLossSum / period;

            for (int i = 0; i < prices.Length; i++)
            {
                if (i < period)
                {
                    rsiValues[i] = 50;
                    continue;
                }

                if (i > period)
                {
                    double change = prices[i] - prices[i - 1];
                    double currentGain = change > 0 ? change : 0;
                    double currentLoss = change < 0 ? -change : 0;

                    avgGain = ((avgGain * (period - 1)) + currentGain) / period;
                    avgLoss = ((avgLoss * (period - 1)) + currentLoss) / period;
                }

                if (avgLoss == 0)
                    rsiValues[i] = 100;

                else
                {
                    double relativeStrength = avgGain / avgLoss;
                    rsiValues[i] = 100 - (100 / (1 + relativeStrength));
                }
            }

            return rsiValues;
        }
    }
}
