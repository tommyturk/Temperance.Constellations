using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.MarketHealth;
using Temperance.Data.Models.Trading;
using Temperance.Utilities.Helpers;
using System.Collections.Generic;

namespace Temperance.Services.Trading.Strategies.Momentum
{
    public class MovingAverageCrossoverStrategy : ISingleAssetStrategy
    {
        public string Name => "Momentum_MACrossover";
        private int _shortTermPeriod;
        private int _longTermPeriod;

        public void Initialize(double initialCapital, Dictionary<string, object> parameters)
        {
            _shortTermPeriod = ParameterHelper.GetParameterOrDefault(parameters, "ShortTermPeriod", 50);
            _longTermPeriod = ParameterHelper.GetParameterOrDefault(parameters, "LongTermPeriod", 200);
        }

        public SignalDecision GenerateSignal(
            in HistoricalPriceModel currentBar,
            Position currentPosition, 
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues,
            MarketHealthScore marketHealth) 
        {
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
            var smaShort_Prev = currentIndicatorValues["SMA_Short_Prev"];
            var smaLong_Prev = currentIndicatorValues["SMA_Long_Prev"];

            if (position.Direction == PositionDirection.Long && smaShort_Prev >= smaLong_Prev && smaShort < smaLong)
                return true;

            if (position.Direction == PositionDirection.Short && smaShort_Prev <= smaLong_Prev && smaShort > smaLong)
                return true;

            return false;
        }

        public string GetExitReason(
            Position position,
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues)
        {
            if (ShouldExitPosition(position, in currentBar, historicalDataWindow, currentIndicatorValues))
                return position.Direction == PositionDirection.Long ? "Death Cross" : "Golden Cross";
           
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
            return maxTradeAllocationInitialCapital;
        }

        public Dictionary<string, object> GetDefaultParameters()
        {
            return new Dictionary<string, object>
            {
                { "ShortTermPeriod", 50 },
                { "LongTermPeriod", 200 }
            };
        }

        public int GetRequiredLookbackPeriod() => _longTermPeriod > 0 ? _longTermPeriod : 200;
        public long GetMinimumAverageDailyVolume() => 100_000;
        public int GetMaxPyramidEntries() => 0;
        public bool ShouldTakePartialProfit(Position position, in HistoricalPriceModel currentBar, Dictionary<string, double> currentIndicatorValues) => false;
        public double GetAtrMultiplier() => 0;
        public double GetStdDevMultiplier() => 0;
        public double[] CalculateRSI(double[] prices, int period) => System.Array.Empty<double>();

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            return activeTrade;
        }
    }
}