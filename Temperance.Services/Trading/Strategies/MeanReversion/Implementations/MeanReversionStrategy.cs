using Microsoft.Extensions.Logging;
using System.Text.Json;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;

namespace Temperance.Services.Trading.Strategies.MeanReversion.Implementation
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
        private decimal _minimumAverageDailyVolume;
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
            {"MinimumAverageDailyVolume", 750000m }
        };

        public void Initialize(decimal initialCapital, Dictionary<string, object> parameters)
        {
            _movingAveragePeriod = GetParameterOrDefault(parameters, "MovingAveragePeriod", 20);
            _stdDevMultiplier = GetParameterOrDefault(parameters, "StdDevMultiplier", 2.0m);
            _rsiPeriod = GetParameterOrDefault(parameters, "RSIPeriod", 14);
            _rsiOversoldThreshold = GetParameterOrDefault(parameters, "RSIOversold", 30m);
            _rsiOverboughtThreshold = GetParameterOrDefault(parameters, "RSIOverbought", 70m);
            _minimumAverageDailyVolume = GetParameterOrDefault(parameters, "MinimumAverageDailyVolume", 750000); // Ensure 750000m is decimal literal

            // Log the initialization (unchanged)
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

        public SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow)
        {
            if (historicalDataWindow.Count < _movingAveragePeriod || historicalDataWindow.Count < _rsiPeriod + 1)
                return SignalDecision.Hold;

            var bbWindowPrices = historicalDataWindow.TakeLast(_movingAveragePeriod).Select(h => h.ClosePrice).ToList();
            if (bbWindowPrices.Count < _movingAveragePeriod) return SignalDecision.Hold;

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
            if (activeTrade == null) return null; // Or throw

            decimal rawExitPrice = currentBar.ClosePrice;

            decimal effectiveExitPrice = _transactionCostService.CalculateExitCost(rawExitPrice, activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short);

            activeTrade.ExitDate = currentBar.Timestamp;
            activeTrade.ExitPrice = effectiveExitPrice;

            decimal profitLoss = 0;
            if (activeTrade.Direction == "Long")
                profitLoss = (activeTrade.ExitPrice.Value - activeTrade.EntryPrice) * activeTrade.Quantity; // Use effective entry/exit
            else if (activeTrade.Direction == "Short")
                profitLoss = (activeTrade.EntryPrice - activeTrade.ExitPrice.Value) * activeTrade.Quantity;

            activeTrade.ProfitLoss = profitLoss;
            activeTrade.TransactionCost = _transactionCostService.CalculateTotalCost(activeTrade.EntryPrice, rawExitPrice, (activeTrade.Direction == "Long" ? SignalDecision.Buy : SignalDecision.Sell), (activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short), activeTrade.Quantity);

            return activeTrade;
        }

        public decimal GetAllocationAmount(
            HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            decimal maxTradeAllocationInitialCapital,
            decimal currentTotalEquity,
            decimal kellyHalfFraction)
        {
            if (historicalDataWindow.Count < _movingAveragePeriod || historicalDataWindow.Count < _rsiPeriod + 1)
                return 0; 

            var bbWindowPrices = historicalDataWindow.TakeLast(_movingAveragePeriod).Select(h => h.ClosePrice).ToList();
            if (bbWindowPrices.Count < _movingAveragePeriod)
                return 0;

            decimal simpleMovingAverage = bbWindowPrices.Average();
            decimal standardDeviation = CalculateStdDev(bbWindowPrices);
            decimal upperBollingerBand = simpleMovingAverage + _stdDevMultiplier * standardDeviation;
            decimal lowerBollingerBand = simpleMovingAverage - _stdDevMultiplier * standardDeviation;

            var rsiWindowPrices = historicalDataWindow.Select(h => h.ClosePrice).ToList();
            List<decimal> rsiValues = CalculateRSI(rsiWindowPrices, _rsiPeriod);
            if (!rsiValues.Any() || rsiValues.Count < historicalDataWindow.Count) 
                return 0;

            decimal currentRelativeStrengthIndex = rsiValues.Last();

            SignalDecision signal = GenerateSignal(currentBar, historicalDataWindow); 

            decimal calculatedAllocationFromStrategyLogic = 0; 

            if (signal == SignalDecision.Buy)
            {
                decimal distanceBelowLowerBand = Math.Max(0, lowerBollingerBand - currentBar.ClosePrice);
                decimal distanceBelowRSIOversold = Math.Max(0, _rsiOversoldThreshold - currentRelativeStrengthIndex);

                if (distanceBelowRSIOversold > 0 && currentBar.ClosePrice < lowerBollingerBand)
                {
                    decimal rsiScalingFactor = Math.Min(1.0m, distanceBelowRSIOversold / (_rsiOversoldThreshold - 0m));
                    calculatedAllocationFromStrategyLogic = maxTradeAllocationInitialCapital * rsiScalingFactor;
                }
            }
            else if (signal == SignalDecision.Sell) 
            {
                decimal distanceAboveUpperBand = Math.Max(0, currentBar.ClosePrice - upperBollingerBand);
                decimal distanceAboveRSIOverbought = Math.Max(0, currentRelativeStrengthIndex - _rsiOverboughtThreshold);

                if (distanceAboveRSIOverbought > 0 && currentBar.ClosePrice > upperBollingerBand)
                {
                    decimal rsiScalingFactor = Math.Min(1.0m, distanceAboveRSIOverbought / (100m - _rsiOverboughtThreshold));
                    calculatedAllocationFromStrategyLogic = maxTradeAllocationInitialCapital * rsiScalingFactor;
                }
            }

            if (calculatedAllocationFromStrategyLogic <= 0)
                return 0;

            decimal kellySizedAllocation = currentTotalEquity * kellyHalfFraction;
            if (kellySizedAllocation < 0) kellySizedAllocation = 0;

            decimal finalAllocationAmount = Math.Min(
                calculatedAllocationFromStrategyLogic, // Strategy's own scaled suggestion
                Math.Min(maxTradeAllocationInitialCapital, kellySizedAllocation) // Capped by 2% initial capital AND Kelly/2 current equity
            );

            if (finalAllocationAmount <= 0)
                return 0;

            _logger.LogDebug("Symbol: {Symbol}, Bar: {Timestamp} - Signal: {Signal}. Strategy Allocation: {StratAlloc:C}, Kelly Alloc: {KellyAlloc:C}, Max Alloc: {MaxAlloc:C}. Final Allocation: {FinalAlloc:C}",
                currentBar.Symbol, currentBar.Timestamp, signal, calculatedAllocationFromStrategyLogic, kellySizedAllocation, maxTradeAllocationInitialCapital, finalAllocationAmount);

            return finalAllocationAmount;
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

            // Handle insufficient data: If we don't have enough prices for the initial period, return an empty list.
            if (prices == null || prices.Count <= period)
            {
                // For consistency with your original padding, we can return a list of 50s.
                // However, a true calculation wouldn't start until 'period' bars are available.
                return Enumerable.Repeat(50m, prices.Count).ToList();
            }

            decimal initialGainsSum = 0;
            decimal initialLossesSum = 0;

            // Iterate from the second price (index 1) up to and including the 'period'-th price.
            // We need 'period' changes, so we look at prices[1] vs prices[0], prices[2] vs prices[1] etc.
            for (int i = 1; i <= period; i++)
            {
                decimal change = prices[i] - prices[i - 1];
                if (change > 0)
                    initialGainsSum += change;
                else
                    initialLossesSum += Math.Abs(change);
            }

            decimal avgGain = initialGainsSum / period;
            decimal avgLoss = initialLossesSum / period;

            decimal initialRS;
            if (avgLoss == 0)
                initialRS = decimal.MaxValue; // Represents infinite strength, leading to RSI 100
            else
                initialRS = avgGain / avgLoss;

            decimal initialRSI;
            if (initialRS == decimal.MaxValue)
                initialRSI = 100m;
            else
                initialRSI = 100m - (100m / (1m + initialRS));

            // Add padding for the bars where RSI cannot be calculated yet
            // and then add the first calculated RSI value.
            // The number of padding values is 'period'.
            for (int i = 0; i < period; i++)
                rsiValues.Add(50m);
            rsiValues[period - 1] = initialRSI;

            // Wilder's smoothing ---
            for (int i = period + 1; i < prices.Count; i++)
            {
                decimal currentChange = prices[i] - prices[i - 1];
                decimal currentGain = currentChange > 0 ? currentChange : 0;
                decimal currentLoss = currentChange < 0 ? Math.Abs(currentChange) : 0;

                // Wilder's Smoothing Formula:
                // New Avg Gain = [(Previous Avg Gain * (period - 1)) + Current Gain] / period
                // New Avg Loss = [(Previous Avg Loss * (period - 1)) + Current Loss] / period
                avgGain = ((avgGain * (period - 1)) + currentGain) / period;
                avgLoss = ((avgLoss * (period - 1)) + currentLoss) / period;

                decimal currentRS;
                if (avgLoss == 0)
                    currentRS = decimal.MaxValue;
                else
                    currentRS = avgGain / avgLoss;

                decimal currentRSI;
                if (currentRS == decimal.MaxValue)
                    currentRSI = 100m;
                else
                    currentRSI = 100m - (100m / (1m + currentRS));
                rsiValues.Add(currentRSI);
            }

            return rsiValues;
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

        public decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocation)
        {
            throw new NotImplementedException();
        }

        private T GetParameterOrDefault<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                _logger.LogWarning("Parameter '{Key}' not found. Using default value: {DefaultValue}", key, defaultValue);
                return defaultValue;
            }

            try
            {
                if (value is JsonElement jsonElement)
                {
                    if (typeof(T) == typeof(decimal))
                    {
                        if (jsonElement.TryGetDecimal(out var decimalValue))
                        {
                            return (T)(object)decimalValue;
                        }
                    }
                    if (typeof(T) == typeof(int))
                    {
                        if (jsonElement.TryGetInt32(out var intValue))
                        {
                            return (T)(object)intValue;
                        }
                    }
                    if (typeof(T) == typeof(double))
                    {
                        if (jsonElement.TryGetDouble(out var doubleValue))
                        {
                            return (T)(object)doubleValue;
                        }
                    }
                    return (T)Convert.ChangeType(jsonElement.ToString(), typeof(T));
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                _logger.LogError(ex, "Parameter '{Key}' has an invalid value '{Value}'. Expected type '{ExpectedType}'. Using default value: {DefaultValue}", key, value, typeof(T).Name, defaultValue);
                return defaultValue;
            }
        }
    }
}
