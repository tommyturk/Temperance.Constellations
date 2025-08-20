using Microsoft.Extensions.Logging;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.MarketHealth;
using Temperance.Data.Models.Trading;
using Temperance.Services.Services.Interfaces;
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
        private int _maxPyramidEntries;
        private double _initialEntryScale;
        private double _stopLossPercentage;
        private int _atrPeriod;
        private double _atrMultiplier;

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
            _atrPeriod = ParameterHelper.GetParameterOrDefault(parameters, "AtrPeriod", 14);
            _atrMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "AtrMultiplier", 2.5);
            _stopLossPercentage = ParameterHelper.GetParameterOrDefault(parameters, "StopLossPercentage", 0.03);
            _maxPyramidEntries = ParameterHelper.GetParameterOrDefault(parameters, "MaxPyramidEntries", 1);
            _initialEntryScale = ParameterHelper.GetParameterOrDefault(parameters, "InitialEntryScale", 1.0);

            _logger.LogInformation($"Initializing {Name} with MA:{_movingAveragePeriod}, SDMult:{_stdDevMultiplier}, RSI:{_rsiPeriod}, RSI Levels:{_rsiOversoldThreshold}/{_rsiOverboughtThreshold}");
        }

        public int GetMaxPyramidEntries() => _maxPyramidEntries;
        public int GetRequiredLookbackPeriod() => Math.Max(_movingAveragePeriod, _rsiPeriod + 1) + 1;
        public long GetMinimumAverageDailyVolume() => (long)_minimumAverageDailyVolume;

        public SignalDecision GenerateSignal(in HistoricalPriceModel currentBar, Position? currentPosition, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, 
            Dictionary<string, double> currentIndicatorValues, MarketHealthScore marketHealth)
        {
            if (currentPosition == null && marketHealth <= MarketHealthScore.Bearish)
                return SignalDecision.Hold;

            if (historicalDataWindow.Count < GetRequiredLookbackPeriod()) return SignalDecision.Hold;

            double lowerBollingerBand = currentIndicatorValues["LowerBand"];
            double upperBollingerBand = currentIndicatorValues["UpperBand"];
            double currentRsi = currentIndicatorValues["RSI"];

            if (currentBar.ClosePrice < lowerBollingerBand && currentRsi < _rsiOversoldThreshold) return SignalDecision.Buy;
            if (currentBar.ClosePrice > upperBollingerBand && currentRsi > _rsiOverboughtThreshold) return SignalDecision.Sell;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(Position position, in HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues)
        {
            double atrValue = currentIndicatorValues.TryGetValue("ATR", out var atr) ? atr : 0;
            if(atrValue > 0)
            {
                double stopLossPrice;
                if(position.Direction == PositionDirection.Long)
                {
                    stopLossPrice = position.AverageEntryPrice - (_atrMultiplier * atrValue);
                    if (currentBar.LowPrice <= stopLossPrice) return true;
                }
                else
                {
                    stopLossPrice = position.AverageEntryPrice + (_atrMultiplier * atrValue);
                    if (currentBar.HighPrice >= stopLossPrice) return true;
                }
            }
            var exitSignal = GenerateSignal(in currentBar, position, historicalDataWindow, currentIndicatorValues, MarketHealthScore.Neutral);
            if (position.Direction == PositionDirection.Long && exitSignal == SignalDecision.Sell) return true;
            if (position.Direction == PositionDirection.Short && exitSignal == SignalDecision.Buy) return true;

            return false;
        }

        public bool ShouldTakePartialProfit(Position position, in HistoricalPriceModel currentBar, Dictionary<string, double> currentIndicatorValues)
        {
            double middleBollingerBand = (currentIndicatorValues["UpperBand"] + currentIndicatorValues["LowerBand"]) / 2;

            if(position.Direction == PositionDirection.Long && currentBar.HighPrice >= middleBollingerBand) return true;

            if(position.Direction == PositionDirection.Short && currentBar.LowPrice <= middleBollingerBand) return true;

            return false;
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            if (activeTrade == null) return null;

            double rawExitPrice = currentBar.ClosePrice;
            var direction = activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short;
            double effectiveExitPrice = _transactionCostService.CalculateExitCost(rawExitPrice, direction);

            activeTrade.ExitDate = currentBar.Timestamp;
            activeTrade.ExitPrice = effectiveExitPrice;

            double profitLoss = direction == PositionDirection.Long
                ? (effectiveExitPrice - activeTrade.EntryPrice) * activeTrade.Quantity
                : (activeTrade.EntryPrice - effectiveExitPrice) * activeTrade.Quantity;

            activeTrade.ProfitLoss = profitLoss - activeTrade.TransactionCost; 

            return activeTrade;
        }

        public double GetAllocationAmount(in HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues, double maxTradeAllocationInitialCapital, double currentTotalEquity,
            double kellyHalfFraction, int currentPyramidEntries, MarketHealthScore marketHealthScore)
        {
            var signal = GenerateSignal(in currentBar, null, historicalDataWindow, currentIndicatorValues, marketHealthScore);
            if (signal == SignalDecision.Hold) return 0;

            double atrValue = currentIndicatorValues.TryGetValue("ATR", out var atr) ? atr : 0;

            const double MIN_VOLATILITY_AS_PRICE_PCT = 0.001;
            double minimumRiskFromPrice = currentBar.ClosePrice * MIN_VOLATILITY_AS_PRICE_PCT;

            double effectiveAtr = Math.Max(atrValue, minimumRiskFromPrice);

            if (effectiveAtr <= 0) 
            {
                _logger.LogError("Effective ATR is zero or negative even after applying floor for {Symbol} at {Timestamp}. Price: {Price}",
                    currentBar.Symbol, currentBar.Timestamp, currentBar.ClosePrice);
                return 0;
            }

            const double BASE_PORTFOLIO_RISK_PER_TRADE = 0.01;

            double marketHealthMultiplier = marketHealthScore switch
            {
                MarketHealthScore.StronglyBullish => 1.2,
                MarketHealthScore.Bullish => 1.0,
                MarketHealthScore.Neutral => 0.75,
                _ => 0.0
            };

            if (marketHealthMultiplier <= 0) return 0;

            double rsiScalingFactor = 0.5;
            double currentRsi = currentIndicatorValues["RSI"];
            if (signal == SignalDecision.Buy)
            {
                double dist = Math.Max(0, _rsiOversoldThreshold - currentRsi);
                rsiScalingFactor = 0.5 + (0.5 * Math.Min(1.0, dist / _rsiOversoldThreshold));
            }
            else if (signal == SignalDecision.Sell)
            {
                double dist = Math.Max(0, currentRsi - _rsiOverboughtThreshold);
                rsiScalingFactor = 0.5 + (0.5 * Math.Min(1.0, dist / (100.0 - _rsiOverboughtThreshold)));
            }

            double adjustedRiskPercentage = BASE_PORTFOLIO_RISK_PER_TRADE * marketHealthMultiplier * rsiScalingFactor;
            double effectiveKellyFraction = Math.Max(0.005, kellyHalfFraction);
            adjustedRiskPercentage = Math.Min(adjustedRiskPercentage, effectiveKellyFraction);
            double finalDollarRisk = currentTotalEquity * adjustedRiskPercentage;

            double riskPerShare = _atrMultiplier * effectiveAtr; 
            if (riskPerShare <= 0) return 0;

            int quantity = (int)Math.Floor(finalDollarRisk / riskPerShare);
            if (quantity <= 0) return 0;

            double allocationAmount = quantity * currentBar.ClosePrice;
            allocationAmount = Math.Min(allocationAmount, maxTradeAllocationInitialCapital);

            if (currentPyramidEntries == 1)
            {
                return allocationAmount * _initialEntryScale;
            }
            else
            {
                int divisor = _maxPyramidEntries - 1 > 0 ? _maxPyramidEntries - 1 : 1;
                return allocationAmount * (1.0 - _initialEntryScale) / divisor;
            }
        }

        //protected double CalculateStopLoss(Position position)
        //{
        //    double stopLossPercentage = 0.05;

        //    if (position.Direction == PositionDirection.Long)
        //        return position.EntryPrice * (1 - stopLossPercentage);
        //    else
        //        return position.EntryPrice * (1 + stopLossPercentage);
        //}

        //protected double CalculateTakeProfit(Position position)
        //{
        //    double takeProfitPercentage = 0.05;

        //    if (position.Direction == PositionDirection.Long)
        //        return position.EntryPrice * (1 + takeProfitPercentage);
        //    else
        //        return position.EntryPrice * (1 - takeProfitPercentage);
        //}

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
        public string GetEntryReason(
           in HistoricalPriceModel currentBar,
           IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
           Dictionary<string, double> currentIndicatorValues)
        {
            double rsi = currentIndicatorValues["RSI"];
            if (currentBar.ClosePrice < currentIndicatorValues["LowerBand"])
            {
                return $"Price ({currentBar.ClosePrice:F2}) below Lower Band and RSI ({rsi:F1}) is oversold.";
            }
            if (currentBar.ClosePrice > currentIndicatorValues["UpperBand"])
            {
                return $"Price ({currentBar.ClosePrice:F2}) above Upper Band and RSI ({rsi:F1}) is overbought.";
            }
            return "Unknown Entry Signal";
        }

        public string GetExitReason(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            // Calculate the SMA for the exit condition
            var smaWindow = historicalDataWindow.Skip(Math.Max(0, historicalDataWindow.Count - _movingAveragePeriod));
            double simpleMovingAverage = smaWindow.Average(b => b.ClosePrice);

            if (currentBar.ClosePrice >= simpleMovingAverage)
            {
                return $"Price ({currentBar.ClosePrice:F2}) reverted to or above mean ({simpleMovingAverage:F2}).";
            }
            if (currentBar.ClosePrice <= simpleMovingAverage)
            {
                return $"Price ({currentBar.ClosePrice:F2}) reverted to or below mean ({simpleMovingAverage:F2}).";
            }
            return "Signal Reversal";
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
