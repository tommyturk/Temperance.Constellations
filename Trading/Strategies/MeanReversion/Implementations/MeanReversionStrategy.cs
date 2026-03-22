using System.Text.Json;
using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Utilities.Helpers;

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
        private int _requiredLookback;

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

            if (historicalDataWindow != null && historicalDataWindow.Count < GetRequiredLookbackPeriod()) return SignalDecision.Hold;

            decimal lowerBollingerBand = currentIndicatorValues["LowerBand"];
            decimal upperBollingerBand = currentIndicatorValues["UpperBand"];
            decimal currentRsi = currentIndicatorValues["RSI"];
            decimal atr = currentIndicatorValues["ATR"];

            // NEW: The Carver Viability Filter
            // We check this BEFORE the RSI/BB signals to save CPU and kill churn
            bool canAffordToTrade = _transactionCostService.IsTradeEconomicallyViable(
                currentBar.Symbol,
                currentBar.ClosePrice,
                atr,
                SignalDecision.Buy, // We test for a Buy viability
                "60min",
                currentBar.Timestamp);

            if (!canAffordToTrade) return SignalDecision.Hold;

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
            // We only take partial profit if we haven't already scaled out
            // (Assuming your Position model tracks if a partial exit has occurred)
            if (position.Quantity <= (position.Quantity / 2)) return false;

            decimal middleBollingerBand = currentIndicatorValues["SMA"];

            // LONG: If the wick (High) touches the SMA, sell half to lock in the "quick" reversion.
            if (position.Direction == PositionDirection.Long && currentBar.HighPrice >= middleBollingerBand)
            {
                _logger.LogInformation("Partial Profit Target (SMA) touched for {Symbol}. Scaling out.", position.Symbol);
                return true;
            }

            // SHORT: If the wick (Low) touches the SMA, cover half.
            if (position.Direction == PositionDirection.Short && currentBar.LowPrice <= middleBollingerBand)
            {
                _logger.LogInformation("Partial Profit Target (SMA) touched for {Symbol}. Scaling out.", position.Symbol);
                return true;
            }

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

        public decimal GetAllocationAmount(
            in PriceModel currentBar,
            IReadOnlyList<PriceModel> historicalDataWindow,
            Dictionary<string, decimal> currentIndicatorValues,
            decimal maxTradeAllocationInitialCapital,
            decimal currentTotalEquity,
            decimal kellyHalfFraction, // The "Historical Edge" from Ludus
            int currentPyramidEntries,
            MarketHealthScore marketHealthScore)
        {
            // --- 1. THE VOLATILITY FLOOR (Carver's North Star) ---
            // We target an annual volatility (Speed Limit) based on our Half-Kelly fraction.
            // If Kelly is high, we target 25% vol. If Kelly is low, we target 10%.
            decimal annualVolTarget = Math.Clamp(kellyHalfFraction, 0.10m, 0.25m);
            decimal dailyPortfolioRiskTarget = (currentTotalEquity * annualVolTarget) / 16.0m;

            // --- 2. THE INSTRUMENT WIGGLE (ATR) ---
            // How much does this specific stock "move" in dollars per day?
            decimal atr = currentIndicatorValues.TryGetValue("ATR", out var a) ? a : currentBar.ClosePrice * 0.02m;
            if (atr <= 0) return 0;

            // --- 3. THE ENVIRONMENT MULTIPLIER (MarketHealth) ---
            // Carver doesn't stop trading in bad markets, but he "Downshifts."
            decimal marketMultiplier = marketHealthScore switch
            {
                MarketHealthScore.StronglyBullish => 1.25m, // "Aggressive"
                MarketHealthScore.Bullish => 1.00m, // "Normal"
                MarketHealthScore.Neutral => 0.50m, // "Caution"
                MarketHealthScore.Bearish => 0.25m, // "Defensive"
                MarketHealthScore.StronglyBearish => 0.00m, // "Stop"
                _ => 0.00m
            };

            if (marketMultiplier <= 0) return 0;

            // --- 4. THE CONVICTION SCALING (RSI Forecast) ---
            // A Carver 'Forecast' scales the bet based on signal strength.
            // If RSI is at 30 (entry), that's a 1.0x bet. If RSI is at 10, it's a 2.0x bet.
            decimal currentRsi = currentIndicatorValues["RSI"];
            decimal rsiConviction = 1.0m;

            if (currentRsi < _rsiOversoldThreshold)
            {
                // Calculate how "Deep" the oversold condition is.
                decimal oversoldDepth = (_rsiOversoldThreshold - currentRsi) / _rsiOversoldThreshold;
                rsiConviction = 1.0m + (oversoldDepth * 2.0m); // Max 3.0x multiplier for extreme RSI
            }

            // --- 5. THE DIVERSIFICATION MULTIPLIER (IDM) ---
            // Since you hold multiple symbols, they provide an "Internal Diversification Multiplier."
            const decimal IDM = 1.4m;
            const decimal TARGET_ASSETS = 20.0m; // Aim for 20 diversified positions

            // --- 6. THE FINAL CALCULATION ---
            // Base Carver Size: (Daily Risk / Assets) / ATR
            decimal dollarRiskPerAsset = (dailyPortfolioRiskTarget * IDM) / TARGET_ASSETS;

            // Final Sizing: (Base Size) * (Market Environment) * (Signal Conviction)
            int targetQuantity = (int)Math.Floor((dollarRiskPerAsset / atr) * marketMultiplier * rsiConviction);

            decimal finalAllocation = targetQuantity * currentBar.ClosePrice;

            // --- 7. THE SAFETY GATES ---
            // A. Never exceed the Max Cap per trade (e.g., 10% of total equity)
            decimal maxCap = currentTotalEquity * 0.10m;
            finalAllocation = Math.Min(finalAllocation, maxCap);

            // B. Liquidity Check: Never trade more than 1% of the average daily volume
            decimal avgVolumeValue = _minimumAverageDailyVolume * currentBar.ClosePrice;
            finalAllocation = Math.Min(finalAllocation, avgVolumeValue * 0.01m);

            return finalAllocation;
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

        public string GetExitReason(Position position, in PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, Dictionary<string, decimal> currentIndicatorValues)
        {
            // --- Layer 1: Protective Stop-Loss ---
            if (position.Direction == PositionDirection.Long && currentBar.LowPrice <= position.StopLossPrice)
            {
                return $"ATR Stop-Loss hit at {currentBar.LowPrice:F2}";
            }
            if (position.Direction == PositionDirection.Short && currentBar.HighPrice >= position.StopLossPrice)
            {
                return $"ATR Stop-Loss hit at {currentBar.HighPrice:F2}";
            }

            // --- Layer 2: Full Reversion Target (End of Bar Confirmation) ---
            decimal movingAverage = currentIndicatorValues["SMA"];
            if (position.Direction == PositionDirection.Long && currentBar.ClosePrice >= movingAverage)
            {
                return $"Full Reversion Confirmed at SMA ({movingAverage:F2})";
            }
            if (position.Direction == PositionDirection.Short && currentBar.ClosePrice <= movingAverage)
            {
                return $"Full Reversion Confirmed at SMA ({movingAverage:F2})";
            }

            // --- Layer 3: Time Stop ---
            if (position.BarsHeld >= _maxHoldingBars)
            {
                return $"Time Stop hit after {position.BarsHeld} bars";
            }

            return "Hold";
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

      
        // Add this private field to cache the lookback so the engine doesn't have to calculate it on every bar

        public void UpdateParameters(Dictionary<string, object> newParameters)
        {
            if (newParameters == null || newParameters.Count == 0) return;

            T ExtractValue<T>(string key, Func<JsonElement, T> elementExtractor, Func<object, T> fallbackExtractor, T currentVal)
            {
                if (newParameters.TryGetValue(key, out var obj))
                {
                    if (obj is JsonElement element)
                    {
                        try { return elementExtractor(element); }
                        catch { return currentVal; }
                    }
                    try { return fallbackExtractor(obj); }
                    catch { return currentVal; }
                }
                return currentVal;
            }

            // --- Core Indicators (Updated to match Debugger Keys) ---
            _movingAveragePeriod = ExtractValue("MovingAveragePeriod", e => e.GetInt32(), o => Convert.ToInt32(o), _movingAveragePeriod);
            _stdDevMultiplier = ExtractValue("StdDevMultiplier", e => e.GetDecimal(), o => Convert.ToDecimal(o), _stdDevMultiplier);
            _rsiPeriod = ExtractValue("RSIPeriod", e => e.GetInt32(), o => Convert.ToInt32(o), _rsiPeriod);
            _rsiOversoldThreshold = ExtractValue("RSIOversold", e => e.GetDecimal(), o => Convert.ToDecimal(o), _rsiOversoldThreshold);
            _rsiOverboughtThreshold = ExtractValue("RSIOverbought", e => e.GetDecimal(), o => Convert.ToDecimal(o), _rsiOverboughtThreshold);

            // --- Risk & Sizing ---
            _minimumAverageDailyVolume = ExtractValue("MinimumAverageDailyVolume", e => e.GetDecimal(), o => Convert.ToDecimal(o), _minimumAverageDailyVolume);
            _maxPyramidEntries = ExtractValue("MaxPyramidEntries", e => e.GetInt32(), o => Convert.ToInt32(o), _maxPyramidEntries);
            _initialEntryScale = ExtractValue("InitialEntryScale", e => e.GetDecimal(), o => Convert.ToDecimal(o), _initialEntryScale);
            _stopLossPercentage = ExtractValue("StopLossPercentage", e => e.GetDecimal(), o => Convert.ToDecimal(o), _stopLossPercentage);

            // --- Volatility & Time Stops ---
            _atrPeriod = ExtractValue("AtrPeriod", e => e.GetInt32(), o => Convert.ToInt32(o), _atrPeriod);
            _atrMultiplier = ExtractValue("AtrMultiplier", e => e.GetDecimal(), o => Convert.ToDecimal(o), _atrMultiplier);
            _maxHoldingBars = ExtractValue("MaxHoldingBars", e => e.GetInt32(), o => Convert.ToInt32(o), _maxHoldingBars);

            // --- CRITICAL: Recalculate the Lookback Requirement ---
            _requiredLookback = Math.Max(Math.Max(_movingAveragePeriod, _rsiPeriod), _atrPeriod) + 1;
        }

        public void UpdateParameters(Dictionary<string, string> newParameters)
        {
            throw new NotImplementedException();
        }
    }
}
