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

        public SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues)
        {
            double lowerBollingerBand = currentIndicatorValues["LowerBand"];
            double upperBollingerBand = currentIndicatorValues["UpperBand"];
            double currentRelativeStrengthIndex = currentIndicatorValues["RSI"];

            if (currentBar.ClosePrice < lowerBollingerBand && currentRelativeStrengthIndex < _rsiOversoldThreshold)
                return SignalDecision.Buy;

            if (currentBar.ClosePrice > upperBollingerBand && currentRelativeStrengthIndex > _rsiOverboughtThreshold)
                return SignalDecision.Sell;

            return SignalDecision.Hold;
        }

        public SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow)
        {
            if (historicalDataWindow.Count < _movingAveragePeriod || historicalDataWindow.Count < _rsiPeriod + 1)
                return SignalDecision.Hold;

            var bbWindowPrices = historicalDataWindow.TakeLast(_movingAveragePeriod).Select(h => h.ClosePrice).ToList();
            if (bbWindowPrices.Count < _movingAveragePeriod) return SignalDecision.Hold;

            double simpleMovingAverage = bbWindowPrices.Average();
            double standardDeviation = CalculateStdDev(bbWindowPrices);
            double upperBollingerBand = simpleMovingAverage + _stdDevMultiplier * standardDeviation;
            double lowerBollingerBand = simpleMovingAverage - _stdDevMultiplier * standardDeviation;

            var rsiWindowPrices = historicalDataWindow.Select(h => h.ClosePrice).ToArray();
            double[] rsiValues = CalculateRSI(rsiWindowPrices, _rsiPeriod);
            if (rsiValues.Length == 0 || rsiValues.Length< historicalDataWindow.Count)
                return SignalDecision.Hold;

            double currentRelativeStrengthIndex = rsiValues.Last();

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

            double stopLossPrice = CalculateStopLoss(position);
            if (position.Direction == PositionDirection.Long && currentBar.LowPrice <= stopLossPrice) return true;
            if (position.Direction == PositionDirection.Short && currentBar.HighPrice >= stopLossPrice) return true;

            double takeProfitPrice = CalculateTakeProfit(position);
            if (position.Direction == PositionDirection.Long && currentBar.HighPrice >= takeProfitPrice) return true;
            if (position.Direction == PositionDirection.Short && currentBar.LowPrice <= takeProfitPrice) return true;

            return false;
        }

        public bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues)
        {
            var currentSignal = GenerateSignal(currentBar, historicalDataWindow, currentIndicatorValues);

            // Exit on opposing signal
            if (position.Direction == PositionDirection.Long && currentSignal == SignalDecision.Sell)
                return true;
            if (position.Direction == PositionDirection.Short && currentSignal == SignalDecision.Buy)
                return true;

            // --- Stop Loss / Take Profit logic remains the same ---
            double stopLossPrice = CalculateStopLoss(position);
            if (position.Direction == PositionDirection.Long && currentBar.LowPrice <= stopLossPrice) return true;
            if (position.Direction == PositionDirection.Short && currentBar.HighPrice >= stopLossPrice) return true;

            double takeProfitPrice = CalculateTakeProfit(position);
            if (position.Direction == PositionDirection.Long && currentBar.HighPrice >= takeProfitPrice) return true;
            if (position.Direction == PositionDirection.Short && currentBar.LowPrice <= takeProfitPrice) return true;

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
            activeTrade.TotalTransactionCost = _transactionCostService.CalculateTotalCost(activeTrade.EntryPrice, rawExitPrice, (activeTrade.Direction == "Long" ? SignalDecision.Buy : SignalDecision.Sell), (activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short), activeTrade.Quantity);

            return activeTrade;
        }

        public double GetAllocationAmount(
            HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            double maxTradeAllocationInitialCapital,
            double currentTotalEquity,
            double kellyHalfFraction)
        {
            if (historicalDataWindow.Count < GetRequiredLookbackPeriod())
                return 0;

            var bbWindowPrices = historicalDataWindow.TakeLast(_movingAveragePeriod).Select(h => h.ClosePrice).ToList();
            if (bbWindowPrices.Count < _movingAveragePeriod)
                return 0;

            double simpleMovingAverage = bbWindowPrices.Average();
            double standardDeviation = CalculateStdDev(bbWindowPrices);
            double upperBollingerBand = simpleMovingAverage + _stdDevMultiplier * standardDeviation;
            double lowerBollingerBand = simpleMovingAverage - _stdDevMultiplier * standardDeviation;

            var rsiWindowPrices = historicalDataWindow.Select(h => h.ClosePrice).ToArray();
            double[] rsiValues = CalculateRSI(rsiWindowPrices, _rsiPeriod);
            if (!rsiValues.Any() || rsiValues.Length < historicalDataWindow.Count) 
                return 0;

            double currentRelativeStrengthIndex = rsiValues.Last();

            SignalDecision signal = GenerateSignal(currentBar, historicalDataWindow);

            double calculatedAllocationFromStrategyLogic = 0; 

            if (signal == SignalDecision.Buy)
            {
                double distanceBelowLowerBand = Math.Max(0, lowerBollingerBand - currentBar.ClosePrice);
                double distanceBelowRSIOversold = Math.Max(0, _rsiOversoldThreshold - currentRelativeStrengthIndex);

                if (distanceBelowRSIOversold > 0 && currentBar.ClosePrice < lowerBollingerBand)
                {
                    double rsiScalingFactor = Math.Min(1.0, distanceBelowRSIOversold / (_rsiOversoldThreshold - 0));
                    calculatedAllocationFromStrategyLogic = maxTradeAllocationInitialCapital * rsiScalingFactor;
                }
            }
            else if (signal == SignalDecision.Sell) 
            {
                double distanceAboveUpperBand = Math.Max(0, currentBar.ClosePrice - upperBollingerBand);
                double distanceAboveRSIOverbought = Math.Max(0, currentRelativeStrengthIndex - _rsiOverboughtThreshold);

                if (distanceAboveRSIOverbought > 0 && currentBar.ClosePrice > upperBollingerBand)
                {
                    double rsiScalingFactor = Math.Min(1.0, distanceAboveRSIOverbought / (100 - _rsiOverboughtThreshold));
                    calculatedAllocationFromStrategyLogic = maxTradeAllocationInitialCapital * rsiScalingFactor;
                }
            }

            if (calculatedAllocationFromStrategyLogic <= 0)
                return 0;

            double kellySizedAllocation = currentTotalEquity * kellyHalfFraction;
            _logger.LogInformation($"Calculating Kelly: CurrentTotalEquity = {currentTotalEquity}, Kelly Half fraction = {kellyHalfFraction}; KellySizedAllocation = {kellySizedAllocation}");
            
            if (kellySizedAllocation < 0) kellySizedAllocation = 0;

            double finalAllocationAmount = Math.Min(
                calculatedAllocationFromStrategyLogic, 
                Math.Min(maxTradeAllocationInitialCapital, kellySizedAllocation)
            );

            if (finalAllocationAmount <= 0)
                return 0;

            _logger.LogDebug("Symbol: {Symbol}, Bar: {Timestamp} - Signal: {Signal}. Strategy Allocation: {StratAlloc:C}, Kelly Alloc: {KellyAlloc:C}, Max Alloc: {MaxAlloc:C}. Final Allocation: {FinalAlloc:C}",
                currentBar.Symbol, currentBar.Timestamp, signal, calculatedAllocationFromStrategyLogic, kellySizedAllocation, maxTradeAllocationInitialCapital, finalAllocationAmount);

            return finalAllocationAmount;
        }

        private double CalculateStdDev(List<double> values)
        {
            if (values == null || values.Count <= 1)
                return 0;

            double average = values.Average();
            double sumOfSquares = values.Sum(val => Math.Pow((val - average), 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
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

        public string GetEntryReason(HistoricalPriceModel currentBar, List<HistoricalPriceModel> dataWindow, Dictionary<string, double> currentIndicatorValues)
        {
            currentIndicatorValues.TryGetValue("RSI", out double currentRSI);
            currentIndicatorValues.TryGetValue("LowerBand", out double currentLowerBand);
            currentIndicatorValues.TryGetValue("UpperBand", out double currentUpperBand);

            if (currentBar.ClosePrice < currentLowerBand && currentRSI < _rsiOversoldThreshold)
            {
                return $"Price below Lower BB ({currentLowerBand:N2}) and RSI ({currentRSI:N2}) oversold (<{_rsiOversoldThreshold})";
            }
            else if (currentBar.ClosePrice > currentUpperBand && currentRSI > _rsiOverboughtThreshold)
            {
                return $"Price above Upper BB ({currentUpperBand:N2}) and RSI ({currentRSI:N2}) overbought (>{_rsiOverboughtThreshold})";
            }
            return "No specific entry signal reason";
        }

        public string GetExitReason(Position currentPosition, HistoricalPriceModel currentBar, List<HistoricalPriceModel> dataWindow, Dictionary<string, double> currentIndicatorValues)
        {
            currentIndicatorValues.TryGetValue("RSI", out double currentRSI);
            double entryPrice = currentPosition.EntryPrice;
            double currentClose = (double)currentBar.ClosePrice;

            if (currentPosition.Direction == PositionDirection.Long)
            {
                if (currentClose > currentIndicatorValues["RSI"]) // Placeholder for middle band cross
                {
                    return "Price crossed above middle band (SMA)";
                }
                if (currentRSI > _rsiOverboughtThreshold)
                {
                    return $"RSI ({currentRSI:N2}) became overbought (>{_rsiOverboughtThreshold})";
                }
                if (currentClose <= entryPrice * 0.98)
                {
                    return $"Stop Loss Hit (Price: {currentClose:N2} <= {entryPrice * 0.98:N2})";
                }
                if (currentClose >= entryPrice * 1.05)
                {
                    return $"Take Profit Hit (Price: {currentClose:N2} >= {entryPrice * 1.05:N2})";
                }
            }
            else if (currentPosition.Direction == PositionDirection.Short)
            {
                if (currentClose < currentIndicatorValues["RSI"]) // Placeholder for middle band cross
                {
                    return "Price crossed below middle band (SMA)";
                }
                if (currentRSI < _rsiOversoldThreshold)
                {
                    return $"RSI ({currentRSI:N2}) became oversold (<{_rsiOversoldThreshold})";
                }
                if (currentClose >= entryPrice * 1.02)
                {
                    return $"Stop Loss Hit (Price: {currentClose:N2} >= {entryPrice * 1.02:N2})";
                }
                if (currentClose <= entryPrice * 0.95)
                {
                    return $"Take Profit Hit (Price: {currentClose:N2} <= {entryPrice * 0.95:N2})";
                }
            }
            return "No specific exit signal reason";
        }
    }
}
