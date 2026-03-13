using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Utilities.Helpers;
using Temperance.Constellations.Models.MarketHealth;

namespace Temperance.Services.Trading.Strategies.MeanReversion.Implementation
{
    public class MeanReversionStrategy : ISingleAssetStrategy
    {
        public string Name => "MeanReversion_BB_RSI";

        // --- Strategy Parameters ---
        private int _movingAveragePeriod;
        private decimal _stdDevMultiplier;
        private int _rsiPeriod;
        private decimal _rsiOversoldThreshold;
        private decimal _rsiOverboughtThreshold;
        private decimal _minimumAverageDailyVolume;
        private int _maxPyramidEntries;
        private decimal _initialEntryScale;
        private decimal _stopLossPercentage;
        private int _atrPeriod;
        private decimal _atrMultiplier;
        private int _maxHoldingBars;

        private readonly ITransactionCostService _transactionCostService;
        private readonly ILogger<MeanReversionStrategy> _logger;
        public MeanReversionStrategy(ITransactionCostService transactionCostService, ILogger<MeanReversionStrategy> logger)
        {
            _transactionCostService = transactionCostService;
            _logger = logger;
            _logger.LogDebug("MeanReversionStrategy instance created via DI.");
        }
        
        public Dictionary<string, object> GetDefaultParameters() => new()
        {
            { "MovingAveragePeriod", 20 },
            { "StdDevMultiplier", 2.0m },
            { "RSIPeriod", 14 },
            { "RSIOversold", 30m },
            { "RSIOverbought", 70m },
            { "MinimumAverageDailyVolume", 1500000 },
            { "AtrPeriod", 14 },
            { "AtrMultiplier", 2.5 },
            { "MaxHoldingBars", 10 }
        };

        public void Initialize(decimal initialCapital, Dictionary<string, object> parameters)
        {
            _movingAveragePeriod = ParameterHelper.GetParameterOrDefault(parameters, "MovingAveragePeriod", 20);
            _rsiPeriod = ParameterHelper.GetParameterOrDefault(parameters, "RSIPeriod", 14);
            _atrPeriod = ParameterHelper.GetParameterOrDefault(parameters, "AtrPeriod", 14);
            _maxHoldingBars = ParameterHelper.GetParameterOrDefault(parameters, "MaxHoldingBars", 10);
            _maxPyramidEntries = ParameterHelper.GetParameterOrDefault(parameters, "MaxPyramidEntries", 1);

            _stdDevMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "StdDevMultiplier", 2.0m);
            _rsiOversoldThreshold = ParameterHelper.GetParameterOrDefault(parameters, "RSIOversold", 30.0m);
            _rsiOverboughtThreshold = ParameterHelper.GetParameterOrDefault(parameters, "RSIOverbought", 70.0m);
            _minimumAverageDailyVolume = ParameterHelper.GetParameterOrDefault(parameters, "MinimumAverageDailyVolume", 1500000.0m);
            _atrMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "AtrMultiplier", 2.5m);
            _stopLossPercentage = ParameterHelper.GetParameterOrDefault(parameters, "StopLossPercentage", 0.03m);
            _initialEntryScale = ParameterHelper.GetParameterOrDefault(parameters, "InitialEntryScale", 1.0m);

            _logger.LogInformation($"Initializing {Name} with MA:{_movingAveragePeriod}, SDMult:{_stdDevMultiplier}, RSI:{_rsiPeriod}, RSI Levels:{_rsiOversoldThreshold}/{_rsiOverboughtThreshold}");
        }

        public int GetMaxPyramidEntries() => _maxPyramidEntries;
        public int GetRequiredLookbackPeriod() => Math.Max(_movingAveragePeriod, _rsiPeriod + 1) + 1;
        public long GetMinimumAverageDailyVolume() => (long)_minimumAverageDailyVolume;
        public decimal GetAtrMultiplier() => _atrMultiplier;
        public decimal GetStdDevMultiplier() => _stdDevMultiplier;

        public SignalDecision GenerateSignal(in PriceModel currentBar, Position? currentPosition, IReadOnlyList<PriceModel> historicalDataWindow, 
            Dictionary<string, decimal> currentIndicatorValues, MarketHealthScore marketHealth)
        {
            if (currentPosition == null && marketHealth <= MarketHealthScore.Bearish)
                return SignalDecision.Hold;

            if (historicalDataWindow.Count < GetRequiredLookbackPeriod()) return SignalDecision.Hold;

            decimal lowerBollingerBand = currentIndicatorValues["LowerBand"];
            decimal upperBollingerBand = currentIndicatorValues["UpperBand"];
            decimal currentRsi = currentIndicatorValues["RSI"];

            if (currentBar.ClosePrice < lowerBollingerBand && currentRsi < _rsiOversoldThreshold) return SignalDecision.Buy;
            if (currentBar.ClosePrice > upperBollingerBand && currentRsi > _rsiOverboughtThreshold) return SignalDecision.Sell;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(Position position, in PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, Dictionary<string, decimal> currentIndicatorValues)
        {
            return GetExitReason(position, in currentBar, historicalDataWindow, currentIndicatorValues) != "Hold";
        }

        public bool ShouldTakePartialProfit(Position position, in PriceModel currentBar, Dictionary<string, decimal> currentIndicatorValues)
        {
            decimal middleBollingerBand = (currentIndicatorValues["UpperBand"] + currentIndicatorValues["LowerBand"]) / 2;

            if(position.Direction == PositionDirection.Long && currentBar.HighPrice >= middleBollingerBand) return true;

            if(position.Direction == PositionDirection.Short && currentBar.LowPrice <= middleBollingerBand) return true;

            return false;
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, PriceModel currentBar, SignalDecision exitSignal)
        {
            if (activeTrade == null) return null;

            decimal rawExitPrice = currentBar.ClosePrice;
            var direction = activeTrade.Direction == "Long" ? PositionDirection.Long : PositionDirection.Short;
            decimal effectiveExitPrice = _transactionCostService.CalculateExitCost(rawExitPrice, direction);

            activeTrade.ExitDate = currentBar.Timestamp;
            activeTrade.ExitPrice = effectiveExitPrice;

            decimal profitLoss = direction == PositionDirection.Long
                ? (effectiveExitPrice - activeTrade.EntryPrice) * activeTrade.Quantity
                : (activeTrade.EntryPrice - effectiveExitPrice) * activeTrade.Quantity;

            activeTrade.ProfitLoss = profitLoss - activeTrade.TransactionCost; 

            return activeTrade;
        }

        public decimal GetAllocationAmount(in PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow,
            Dictionary<string, decimal> currentIndicatorValues, decimal maxTradeAllocationInitialCapital, decimal currentTotalEquity,
            decimal kellyHalfFraction, int currentPyramidEntries, MarketHealthScore marketHealthScore)
        {
            var signal = GenerateSignal(in currentBar, null, historicalDataWindow, currentIndicatorValues, marketHealthScore);
            if (signal == SignalDecision.Hold) return 0;

            decimal atrValue = currentIndicatorValues.TryGetValue("ATR", out var atr) ? atr : 0;

            const decimal MIN_VOLATILITY_AS_PRICE_PCT = 0.001m;
            decimal minimumRiskFromPrice = currentBar.ClosePrice * MIN_VOLATILITY_AS_PRICE_PCT;

            decimal effectiveAtr = Math.Max(atrValue, minimumRiskFromPrice);

            if (effectiveAtr <= 0) 
            {
                _logger.LogError("Effective ATR is zero or negative even after applying floor for {Symbol} at {Timestamp}. Price: {Price}",
                    currentBar.Symbol, currentBar.Timestamp, currentBar.ClosePrice);
                return 0;
            }

            const decimal BASE_PORTFOLIO_RISK_PER_TRADE = 0.01m;

            decimal marketHealthMultiplier = marketHealthScore switch
            {
                MarketHealthScore.StronglyBullish => 1.2m,
                MarketHealthScore.Bullish => 1.0m,
                MarketHealthScore.Neutral => 0.75m,
                _ => 0.0m
            };

            if (marketHealthMultiplier <= 0) return 0;

            decimal rsiScalingFactor = 0.5m;
            decimal currentRsi = currentIndicatorValues["RSI"];
            if (signal == SignalDecision.Buy)
            {
                decimal dist = Math.Max(0, _rsiOversoldThreshold - currentRsi);
                rsiScalingFactor = 0.5m + (0.5m * Math.Min(1.0m, dist / _rsiOversoldThreshold));
            }
            else if (signal == SignalDecision.Sell)
            {
                decimal dist = Math.Max(0, currentRsi - _rsiOverboughtThreshold);
                rsiScalingFactor = 0.5m + (0.5m * Math.Min(1.0m, dist / (100.0m - _rsiOverboughtThreshold)));
            }

            decimal adjustedRiskPercentage = BASE_PORTFOLIO_RISK_PER_TRADE * marketHealthMultiplier * rsiScalingFactor;
            decimal effectiveKellyFraction = Math.Max(0.005m, kellyHalfFraction);
            adjustedRiskPercentage = Math.Min(adjustedRiskPercentage, effectiveKellyFraction);
            decimal finalDollarRisk = currentTotalEquity * adjustedRiskPercentage;

            decimal riskPerShare = _atrMultiplier * effectiveAtr; 
            if (riskPerShare <= 0) return 0;

            int quantity = (int)Math.Floor(finalDollarRisk / riskPerShare);
            if (quantity <= 0) return 0;

            decimal allocationAmount = quantity * currentBar.ClosePrice;
            allocationAmount = Math.Min(allocationAmount, maxTradeAllocationInitialCapital);

            if (currentPyramidEntries == 1)
            {
                return allocationAmount * _initialEntryScale;
            }
            else
            {
                int divisor = _maxPyramidEntries - 1 > 0 ? _maxPyramidEntries - 1 : 1;
                return allocationAmount * (1.0m - _initialEntryScale) / divisor;
            }
        }

        //protected decimal CalculateStopLoss(Position position)
        //{
        //    decimal stopLossPercentage = 0.05;

        //    if (position.Direction == PositionDirection.Long)
        //        return position.EntryPrice * (1 - stopLossPercentage);
        //    else
        //        return position.EntryPrice * (1 + stopLossPercentage);
        //}

        //protected decimal CalculateTakeProfit(Position position)
        //{
        //    decimal takeProfitPercentage = 0.05;

        //    if (position.Direction == PositionDirection.Long)
        //        return position.EntryPrice * (1 + takeProfitPercentage);
        //    else
        //        return position.EntryPrice * (1 - takeProfitPercentage);
        //}

        public decimal[] CalculateRSI(decimal[] prices, int period)
        {
            if (prices == null || prices.Length <= period)
            {
                var neutralRsi = new decimal[prices?.Length ?? 0];
                Array.Fill(neutralRsi, 50);
                return neutralRsi;
            }

            var rsiValues = new decimal[prices.Length];
            decimal initialGainSum = 0;
            decimal initialLossSum = 0;

            for (int i = 1; i <= period; i++)
            {
                decimal change = prices[i] - prices[i - 1];
                if (change > 0)
                {
                    initialGainSum += change;
                }
                else
                {
                    initialLossSum -= change;
                }
            }

            decimal avgGain = initialGainSum / period;
            decimal avgLoss = initialLossSum / period;

            for (int i = 0; i < prices.Length; i++)
            {
                if (i < period)
                {
                    rsiValues[i] = 50;
                    continue;
                }

                if (i > period)
                {
                    decimal change = prices[i] - prices[i - 1];
                    decimal currentGain = change > 0 ? change : 0;
                    decimal currentLoss = change < 0 ? -change : 0;

                    avgGain = ((avgGain * (period - 1)) + currentGain) / period;
                    avgLoss = ((avgLoss * (period - 1)) + currentLoss) / period;
                }

                if (avgLoss == 0)
                    rsiValues[i] = 100;

                else
                {
                    decimal relativeStrength = avgGain / avgLoss;
                    rsiValues[i] = 100 - (100 / (1 + relativeStrength));
                }
            }

            return rsiValues;
        }
        public string GetEntryReason(
           in PriceModel currentBar,
           IReadOnlyList<PriceModel> historicalDataWindow,
           Dictionary<string, decimal> currentIndicatorValues)
        {
            decimal rsi = currentIndicatorValues["RSI"];
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
            in PriceModel currentBar,
            IReadOnlyList<PriceModel> historicalDataWindow,
            Dictionary<string, decimal> currentIndicatorValues)
        {
            // Calculate the SMA for the exit condition
            var smaWindow = historicalDataWindow.Skip(Math.Max(0, historicalDataWindow.Count - _movingAveragePeriod));
            decimal simpleMovingAverage = smaWindow.Average(b => b.ClosePrice);

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

        public string GetEntryReason(PriceModel currentBar, List<PriceModel> dataWindow, Dictionary<string, decimal> currentIndicatorValues)
        {
            currentIndicatorValues.TryGetValue("RSI", out decimal currentRSI);
            currentIndicatorValues.TryGetValue("LowerBand", out decimal currentLowerBand);
            currentIndicatorValues.TryGetValue("UpperBand", out decimal currentUpperBand);

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

        public string GetExitReason(Position position, in PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, Dictionary<string, decimal> currentIndicatorValues)
        {
            // --- Layer 1: Protective Stop-Loss (Thesis is Wrong) ---
            if (position.Direction == PositionDirection.Long && currentBar.LowPrice <= position.StopLossPrice)
            {
                return $"ATR Stop-Loss hit at {currentBar.LowPrice:F2}";
            }
            if (position.Direction == PositionDirection.Short && currentBar.HighPrice >= position.StopLossPrice)
            {
                return $"ATR Stop-Loss hit at {currentBar.HighPrice:F2}";
            }

            // --- Layer 2: Profit Target at the Mean (Thesis is Correct) ---
            decimal movingAverage = currentIndicatorValues["SMA"];
            if (position.Direction == PositionDirection.Long && currentBar.HighPrice >= movingAverage)
            {
                return $"Profit Target hit at SMA ({movingAverage:F2})";
            }
            if (position.Direction == PositionDirection.Short && currentBar.LowPrice <= movingAverage)
            {
                return $"Profit Target hit at SMA ({movingAverage:F2})";
            }

            // --- Layer 3: Time-Based Stop (Thesis has Expired) ---
            if (position.BarsHeld >= _maxHoldingBars)
            {
                return $"Time Stop hit after {position.BarsHeld} bars";
            }

            // --- Final Check: Signal Reversal ---
            var exitSignal = GenerateSignal(in currentBar, position, historicalDataWindow, currentIndicatorValues, MarketHealthScore.Neutral);
            if ((position.Direction == PositionDirection.Long && exitSignal == SignalDecision.Sell) ||
                (position.Direction == PositionDirection.Short && exitSignal == SignalDecision.Buy))
            {
                return "Signal Reversal";
            }

            return "Hold"; // No exit condition met
        }
    }
}
