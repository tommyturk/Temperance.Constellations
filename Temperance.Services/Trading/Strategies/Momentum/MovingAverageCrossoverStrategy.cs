using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.MarketHealth;
using Temperance.Data.Models.Trading;
using Temperance.Utilities.Helpers;

namespace Temperance.Services.Trading.Strategies.Momentum
{
    public class MovingAverageCrossoverStrategy : ISingleAssetStrategy
    {
        public string Name => "Momentum_MACrossover";
        private int _shortTermPeriod;
        private int _longTermPeriod;
        private double _atrMultiplier; 
        private MarketHealthScore _marketHealthThreshold; 
        public void Initialize(double initialCapital, Dictionary<string, object> parameters)
        {
            _shortTermPeriod = ParameterHelper.GetParameterOrDefault(parameters, "ShortTermPeriod", 50);
            _longTermPeriod = ParameterHelper.GetParameterOrDefault(parameters, "LongTermPeriod", 200);
            _atrMultiplier = ParameterHelper.GetParameterOrDefault(parameters, "AtrMultiplier", 2.5);
            _marketHealthThreshold = ParameterHelper.GetParameterOrDefault(parameters, "MarketHealthThreshold", MarketHealthScore.Neutral);
        }

        public SignalDecision GenerateSignal(
            in HistoricalPriceModel currentBar,
            Position currentPosition,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues,
            MarketHealthScore marketHealth)
        {
            if (marketHealth < _marketHealthThreshold)
                return SignalDecision.Hold;

            if (currentPosition != null)
                return SignalDecision.Hold;

            var smaShort = currentIndicatorValues["SMA_Short"];
            var smaLong = currentIndicatorValues["SMA_Long"];
            var smaShort_Prev = currentIndicatorValues["SMA_Short_Prev"];
            var smaLong_Prev = currentIndicatorValues["SMA_Long_Prev"];

            if (smaShort_Prev <= smaLong_Prev && smaShort > smaLong)
                return SignalDecision.Buy;

            if (smaShort_Prev >= smaLong_Prev && smaShort < smaLong)
                return SignalDecision.Sell;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(
            Position position,
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            var smaShort = currentIndicatorValues["SMA_Short"];
            var smaLong = currentIndicatorValues["SMA_Long"];
            var atr = currentIndicatorValues["ATR"];

            if (position.Direction == PositionDirection.Long)
            {
                var stopLossPrice = position.EntryPrice - (_atrMultiplier * atr);
                if (currentBar.LowPrice <= stopLossPrice)
                    return true;
            }
            else 
            {
                var stopLossPrice = position.EntryPrice + (_atrMultiplier * atr);
                if (currentBar.HighPrice >= stopLossPrice)
                {
                    return true;
                }
            }

            // Original exit logic (Death Cross) can be used as a secondary exit
            // if (position.Direction == PositionDirection.Long && smaShort < smaLong) return true;
            // if (position.Direction == PositionDirection.Short && smaShort > smaLong) return true;

            return false;
        }

        public string GetExitReason(
            Position position,
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            if (ShouldExitPosition(position, in currentBar, historicalDataWindow, currentIndicatorValues))
                return "ATR Stop-Loss Hit";
            return "Hold";
        }

        public string GetEntryReason(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            return "MA Crossover Signal";
        }

        public string GetEntryReason(
            HistoricalPriceModel currentBar,
            List<HistoricalPriceModel> dataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            return GetEntryReason(in currentBar, dataWindow, currentIndicatorValues);
        }

        public double GetAllocationAmount(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues,
            double maxTradeAllocationInitialCapital,
            double currentTotalEquity,
            double kellyHalfFraction, 
            int currentPyramidEntries,
            MarketHealthScore marketHealth)
        {
            if (kellyHalfFraction <= 0)
                return 0;

            double kellySizedCapital = currentTotalEquity * kellyHalfFraction;

            double finalCapitalForSizing = Math.Min(kellySizedCapital, maxTradeAllocationInitialCapital);

            var atr = currentIndicatorValues["ATR"];
            if (atr == 0) return 0;

            double riskAmountPerShare = atr * _atrMultiplier;
            if (riskAmountPerShare == 0) return 0;

            double numShares = finalCapitalForSizing / riskAmountPerShare;

            return numShares * currentBar.ClosePrice;
        }

        public Dictionary<string, object> GetDefaultParameters()
        {
            return new Dictionary<string, object>
            {
                { "ShortTermPeriod", 50 },
                { "LongTermPeriod", 200 },
                { "AtrMultiplier", 2.5 },
                { "MarketHealthThreshold", 60 }
            };
        }

        public int GetRequiredLookbackPeriod() => _longTermPeriod > 0 ? _longTermPeriod : 200;
        public long GetMinimumAverageDailyVolume() => 100_000;
        public int GetMaxPyramidEntries() => 0;
        public bool ShouldTakePartialProfit(Position position, in HistoricalPriceModel currentBar, Dictionary<string, double> currentIndicatorValues) => false;
        public double GetAtrMultiplier() => 0;
        public double GetStdDevMultiplier() => 0;
        public double[] CalculateRSI(double[] prices, int period) => Array.Empty<double>();

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            return activeTrade;
        }
    }
}