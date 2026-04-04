using System.Text.Json;
using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Utilities.Helpers;
using Temperance.Constellations.Models.Policy;

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

        // --- STRATEGY DNA PARAMETERS ---
        // How much pain are we willing to endure annually? (e.g., 0.15 = 15%)
        private readonly decimal TargetAnnualVolatility = 0.15m;

        // The absolute maximum leverage the prime broker allows us to use
        private readonly decimal MaxLeverageCap = 2.0m;

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
            _maxHoldingBars = ParameterHelper.GetParameterOrDefault(parameters, "MaxHoldingBars", 14);
            _maxPyramidEntries = ParameterHelper.GetParameterOrDefault(parameters, "MaxPyramidEntries", 1);

            _stdDevMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "StdDevMultiplier", 2.0m);
            _rsiOversoldThreshold = ParameterHelper.GetParameterOrDefault(parameters, "RSIOversold", 30.0m);
            _rsiOverboughtThreshold = ParameterHelper.GetParameterOrDefault(parameters, "RSIOverbought", 70.0m);
            _minimumAverageDailyVolume = ParameterHelper.GetParameterOrDefault(parameters, "MinimumAverageDailyVolume", 1500000.0m);
            _atrMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "AtrMultiplier", 2.5m);
            _stopLossPercentage = ParameterHelper.GetParameterOrDefault(parameters, "StopLossPercentage", 0.03m);
            _initialEntryScale = ParameterHelper.GetParameterOrDefault(parameters, "InitialEntryScale", 1.0m);
        }

        public int GetMaxPyramidEntries() => _maxPyramidEntries;
        public int GetRequiredLookbackPeriod() => Math.Max(_movingAveragePeriod, _rsiPeriod + 1) + 1;
        public long GetMinimumAverageDailyVolume() => (long)_minimumAverageDailyVolume;
        public decimal GetAtrMultiplier() => _atrMultiplier;
        public decimal GetStdDevMultiplier() => _stdDevMultiplier;

        public SignalDecision GenerateSignal(
            in PriceModel currentBar,
            Position? currentPosition,
            IReadOnlyList<PriceModel>? historicalDataWindow,
            Dictionary<string, decimal> currentIndicatorValues,
            MarketHealthScore marketHealth)
        {
            // ==============================================================
            // 1. STRATEGY VITALS
            // ==============================================================
            decimal lowerBollingerBand = currentIndicatorValues["LowerBand"];
            decimal upperBollingerBand = currentIndicatorValues["UpperBand"];
            decimal currentRsi = currentIndicatorValues["RSI"];
            decimal atr = currentIndicatorValues["ATR"];

            // Safely extract the previous RSI (defaults to neutral 50 if missing on bar 1)
            decimal rsiPrev = currentIndicatorValues.TryGetValue("RSI_Prev", out var prev) ? prev : 50m;

            // ==============================================================
            // 2. ECONOMIC VIABILITY
            // ==============================================================
            bool canAffordToTrade = _transactionCostService.IsTradeEconomicallyViable(
                currentBar.Symbol,
                currentBar.ClosePrice,
                atr,
                SignalDecision.Buy,
                "60min",
                currentBar.Timestamp);

            if (!canAffordToTrade) return SignalDecision.Hold;

            // ==============================================================
            // 3. THE CATASTROPHIC CIRCUIT BREAKER (Macro Veto)
            // Must be checked BEFORE any buy signals are generated!
            // ==============================================================
            if (currentPosition == null && marketHealth <= MarketHealthScore.StronglyBearish)
            {
                return SignalDecision.Hold; // Block all new entries
            }

            // ==============================================================
            // 4. ENTRY LOGIC: ARMORED LUDUS (v3.2 Spatial Stretch)
            // ==============================================================
            bool hasMasterTrend = currentIndicatorValues.TryGetValue("SMA_Long", out decimal longTermTrend) && longTermTrend > 0;

            // The CPO optimized variable (Ludus should optimize this between 0.5 and 2.5)
            decimal stretchMultiplier = 1.0m;

            // Calculate the 'Capitulation Band'
            decimal capitulationBand = lowerBollingerBand - (atr * stretchMultiplier);

            // --- LONG ENTRY (Catch the extreme knife, in an uptrend) ---
            bool isExtremeStretch = currentBar.ClosePrice < capitulationBand;
            bool isOversoldRSI = currentRsi < _rsiOversoldThreshold;

            if (isExtremeStretch && isOversoldRSI)
            {
                if (!hasMasterTrend || currentBar.ClosePrice > longTermTrend)
                    return SignalDecision.Buy;
            }

            // --- SHORT ENTRY (Sell the extreme blow-off top) ---
            decimal euphoriaBand = upperBollingerBand + (atr * stretchMultiplier);
            bool isExtremeEuphoria = currentBar.ClosePrice > euphoriaBand;
            bool isOverboughtRSI = currentRsi > _rsiOverboughtThreshold;

            if (isExtremeEuphoria && isOverboughtRSI)
            {
                if (!hasMasterTrend || currentBar.ClosePrice < longTermTrend)
                    return SignalDecision.Sell;
            }

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
            decimal expectedSharpe,
            int rawMacroScore,
            decimal dynamicIdm,
            int activePortfolioSize)
        {
            decimal atr = currentIndicatorValues.TryGetValue("ATR", out var a) ? a : currentBar.ClosePrice * 0.02m;
            if (currentBar.ClosePrice <= 0 || atr <= 0) return 0m;

            decimal dailyVolatilityPct = atr / currentBar.ClosePrice;
            decimal assetAnnualVolatility = dailyVolatilityPct * (decimal)Math.Sqrt(252);

            // =====================================================================
            // 1. INCREASE RISK TARGET (The "Heat" Dial)
            // Moving from 0.35 to 0.60 allows the portfolio to be much more aggressive
            // =====================================================================
            decimal targetAnnualVolatility = 0.60m;

            // We use a "Floor" for IDM to ensure we don't over-dilute when N is large
            decimal effectiveN = (decimal)activePortfolioSize / Math.Max(1.5m, dynamicIdm);
            decimal perAssetTargetVol = targetAnnualVolatility / Math.Max(1.0m, effectiveN);

            if (assetAnnualVolatility < 0.01m) assetAnnualVolatility = 0.01m;

            decimal impliedWeight = perAssetTargetVol / assetAnnualVolatility;
            decimal rawDollarAllocation = currentTotalEquity * impliedWeight;

            // =====================================================================
            // 2. CONVICTION SCALING (The RSI Stretch)
            // If we are deep in RSI oversold/overbought, we want to put MORE money to work
            // =====================================================================
            decimal currentRsi = currentIndicatorValues["RSI"];
            decimal convictionBoost = 1.0m;
            if (currentRsi < 25m) convictionBoost = 1.5m; // Panic Boost
            if (currentRsi > 75m) convictionBoost = 1.5m; // Euphoria Boost

            // =====================================================================
            // 3. THE ENVIRONMENT MULTIPLIER (Sigmoid)
            // =====================================================================
            double score = (double)rawMacroScore;
            decimal marketMultiplier = (decimal)(1.5 / (1.0 + Math.Exp(-0.5 * score)));

            if (marketMultiplier <= 0.2m) return 0m;

            // Apply conviction and environment
            decimal finalAllocation = rawDollarAllocation * marketMultiplier * convictionBoost;

            // Safety Gates: Hard cap at 15% to prevent one bad ETF from ruining the run
            decimal maxCap = currentTotalEquity * 0.15m;
            finalAllocation = Math.Min(finalAllocation, maxCap);

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
            // ==============================================================
            // 1. THE 14-BAR TIME STOP (The Guillotine)
            // ==============================================================
            var hoursInTrade = (currentBar.Timestamp - position.InitialEntryDate).TotalHours;
            decimal atr = currentIndicatorValues.TryGetValue("ATR", out var a) ? a : currentBar.ClosePrice * 0.02m;
            decimal stopDistance = atr * _atrMultiplier;

            if (hoursInTrade >= 14)
            {
                return "Time Stop hit after 14 bars";
            }

            decimal candleBody = Math.Abs(currentBar.ClosePrice - currentBar.OpenPrice);
            decimal barVelocityInAtr = atr > 0 ? candleBody / atr : 0;

            // If we are in a trade and a catastrophic bar occurs AGAINST our direction, eject immediately.
            if (barVelocityInAtr >= 3.0m)
            {
                if (position.Direction == PositionDirection.Long && currentBar.ClosePrice < currentBar.OpenPrice)
                {
                    return $"Velocity Ejector: Catastrophic Bearish Bar ({barVelocityInAtr:F1} ATR)";
                }
                if (position.Direction == PositionDirection.Short && currentBar.ClosePrice > currentBar.OpenPrice)
                {
                    return $"Velocity Ejector: Catastrophic Bullish Bar ({barVelocityInAtr:F1} ATR)";
                }
            }

            // ==============================================================
            // 2. PROTECTIVE VOLATILITY STOP (ATR)
            // ==============================================================

            if (position.Direction == PositionDirection.Long)
            {
                decimal hardStop = position.AverageEntryPrice - stopDistance;
                if (currentBar.LowPrice <= hardStop) return $"ATR Stop-Loss Hit ({_atrMultiplier}x)";
            }
            else // Short
            {
                decimal hardStop = position.AverageEntryPrice + stopDistance;
                if (currentBar.HighPrice >= hardStop) return $"ATR Stop-Loss Hit ({_atrMultiplier}x)";
            }

            // ==============================================================
            // 3. FULL REVERSION TARGET (WITH THE RATCHET)
            // ==============================================================
            decimal movingAverage = currentIndicatorValues["SMA"];
            decimal rsi = currentIndicatorValues["RSI"];

            // --- LONG EXITS ---
            if (position.Direction == PositionDirection.Long && currentBar.ClosePrice >= movingAverage)
            {
                // 1. Momentum is still aggressively ripping. Hold the winner.
                if (rsi >= 65m) return "Hold";

                // 2. Momentum peaked and is now exhausting. Lock in the fat tail.
                if (rsi < 65m && rsi >= 55m) return $"Ratchet Exit: Momentum Exhaustion (RSI {rsi:F1})";

                // 3. Standard Reversion
                return $"Full Reversion Confirmed at SMA ({movingAverage:F2})";
            }

            // --- SHORT EXITS ---
            if (position.Direction == PositionDirection.Short && currentBar.ClosePrice <= movingAverage)
            {
                // 1. Momentum is still aggressively crashing. Hold the winner.
                if (rsi <= 35m) return "Hold";

                // 2. Momentum bottomed and is recovering. Lock in the fat tail.
                if (rsi > 35m && rsi <= 45m) return $"Ratchet Exit: Momentum Exhaustion (RSI {rsi:F1})";

                // 3. Standard Reversion
                return $"Full Reversion Confirmed at SMA ({movingAverage:F2})";
            }

            // --- DISABLED FOR NAKED BASELINE: Old Bar-Based Time Stop ---
            // if (position.BarsHeld >= _maxHoldingBars)
            // {
            //     return $"Time Stop hit after {position.BarsHeld} bars";
            // }

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

        public decimal CalculatePositionSize(List<decimal> historicalPrices, decimal currentPortfolioEquity, decimal currentTotalExposure)
        {
            // 1. Calculate Daily Returns
            var dailyReturns = new List<double>();
            for (int i = 1; i < historicalPrices.Count; i++)
            {
                // Log returns are standard for quantitative volatility math
                double logReturn = Math.Log((double)(historicalPrices[i] / historicalPrices[i - 1]));
                dailyReturns.Add(logReturn);
            }

            // 2. Calculate Standard Deviation of Daily Returns
            double averageReturn = dailyReturns.Average();
            double sumOfSquaredDifferences = dailyReturns.Sum(r => Math.Pow(r - averageReturn, 2));
            double dailyVariance = sumOfSquaredDifferences / dailyReturns.Count;
            double dailyVolatility = Math.Sqrt(dailyVariance);

            // 3. Annualize the Asset Volatility (assume 252 trading days)
            decimal assetAnnualVolatility = (decimal)(dailyVolatility * Math.Sqrt(252));

            // Prevent divide-by-zero if an asset is frozen
            if (assetAnnualVolatility == 0) return 0m;

            // 4. THE CARVER EQUATION: Weight = Target Vol / Asset Vol
            decimal impliedWeight = TargetAnnualVolatility / assetAnnualVolatility;

            // 5. Calculate Raw Dollar Allocation
            decimal rawDollarAllocation = currentPortfolioEquity * impliedWeight;

            // 6. THE MARGIN GOVERNOR (The Reality Check)
            // How much room do we actually have left before we hit our leverage ceiling?
            decimal maxAllowableExposure = currentPortfolioEquity * MaxLeverageCap;
            decimal availableCapitalSpace = maxAllowableExposure - currentTotalExposure;

            // Return whichever is smaller: what the math wants, or what the broker allows.
            decimal finalAllocation = Math.Min(rawDollarAllocation, Math.Max(0, availableCapitalSpace));

            return finalAllocation;
        }
    }
}

