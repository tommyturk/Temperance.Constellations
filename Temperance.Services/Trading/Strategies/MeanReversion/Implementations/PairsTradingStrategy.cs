using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;
using Temperance.Utilities.Helpers;

namespace Temperance.Services.Trading.Strategies.MeanReversion.Implementations
{
    public class PairsTradingStrategy : IPairTradingStrategy
    {
        public string Name => "PairsTrading_Cointegration";

        private string _symbolA;
        private string _symbolB;
        private double _hedgeRatio;
        private int _spreadLookbackPeriod;
        private double _entryZScoreThreshold;
        private double _exitZScoreThreshold;

        public void Initialize(double initialCapital, Dictionary<string, object> parameters)
        {
            _symbolA = ParameterHelper.GetParameterOrDefault<string>(parameters, "SymbolA", string.Empty);
            _symbolB = ParameterHelper.GetParameterOrDefault<string>(parameters, "SymbolB", string.Empty);
            _hedgeRatio = ParameterHelper.GetParameterOrDefault<double>(parameters, "HedgeRatio", 0);
            _spreadLookbackPeriod = ParameterHelper.GetParameterOrDefault<int>(parameters, "SpreadLookbackPeriod", 60);
            _entryZScoreThreshold = ParameterHelper.GetParameterOrDefault<double>(parameters, "EntryZScoreThreshold", 2.0);
            _exitZScoreThreshold = ParameterHelper.GetParameterOrDefault<double>(parameters, "ExitZScoreThreshold", 0.5);
        }

        public int GetRequiredLookbackPeriod() => _spreadLookbackPeriod;

        public SignalDecision GenerateSignal(HistoricalPriceModel currentBarA,
            HistoricalPriceModel currentBarB, Dictionary<string, double> currentIndicatorValues)
        {
            if (!currentIndicatorValues.TryGetValue("ZScore", out double zScore))
                return SignalDecision.Hold;

            if (zScore > _entryZScoreThreshold)
                return SignalDecision.Sell;
            else if (zScore < -_entryZScoreThreshold)
                return SignalDecision.Buy;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(
            Position position,
            HistoricalPriceModel currentBarA,
            HistoricalPriceModel currentBarB,
            Dictionary<string, double> currentIndicatorValues)
        {
            if (!currentIndicatorValues.TryGetValue("ZScore", out double zScore))
                return false;

            if (position.Direction == PositionDirection.Long && zScore >= -_exitZScoreThreshold)
                return true;
            if (position.Direction == PositionDirection.Short && zScore <= _exitZScoreThreshold)
                return true;

            return false;
        }

        private List<double> CalculateSpreadHistory(
        IReadOnlyList<HistoricalPriceModel> dataA,
        IReadOnlyList<HistoricalPriceModel> dataB)
        {
            return dataA.Zip(dataB, (a, b) => (double)a.ClosePrice - (_hedgeRatio * (double)b.ClosePrice)).ToList();
        }

        private double? CalculateCurrentZScore(
            HistoricalPriceModel currentBarA,
            HistoricalPriceModel currentBarB,
            double hedgeRatio,
            IReadOnlyList<HistoricalPriceModel> historicalDataA,
            IReadOnlyList<HistoricalPriceModel> historicalDataB)
        {
            if (historicalDataA.Count < _spreadLookbackPeriod || historicalDataB.Count < _spreadLookbackPeriod)
                return null; 

            var spreadHistory = historicalDataA
                .Zip(historicalDataB, (a, b) => (double)a.ClosePrice - (hedgeRatio * (double)b.ClosePrice))
                .Select(s => (double)s)
                .ToList();

            var stats = new DescriptiveStatistics(spreadHistory);
            var movingAverage = (double)stats.Mean;
            var standardDeviation = (double)stats.StandardDeviation;

            if (standardDeviation == 0)
                return null;

            var currentSpread = (double)currentBarA.ClosePrice - (hedgeRatio * (double)currentBarB.ClosePrice);

            return (currentSpread - movingAverage) / standardDeviation;
        }

        public string GetEntryReason(HistoricalPriceModel barA, HistoricalPriceModel barB, Dictionary<string, double> currentIndicatorValues)
        {
            currentIndicatorValues.TryGetValue("ZScore", out double currentZScore);
            if (currentZScore < -_entryZScoreThreshold)
            {
                return $"Z-Score ({currentZScore:N2}) below entry threshold (-{_entryZScoreThreshold:N2}) - Long Spread";
            }
            else if (currentZScore > _entryZScoreThreshold)
            {
                return $"Z-Score ({currentZScore:N2}) above entry threshold ({_entryZScoreThreshold:N2}) - Short Spread";
            }
            return "No specific entry signal reason for pair";
        }

        public string GetExitReason(Position currentPosition, HistoricalPriceModel barA, HistoricalPriceModel barB, Dictionary<string, double> currentIndicatorValues)
        {
            currentIndicatorValues.TryGetValue("ZScore", out double currentZScore);

            if (currentPosition.Direction == PositionDirection.Long)
            {
                if (currentZScore >= -_exitZScoreThreshold)
                {
                    return $"Z-Score ({currentZScore:N2}) reverted to mean (>= -{_exitZScoreThreshold:N2}) - Exit Long Spread";
                }
                if (currentZScore >= _entryZScoreThreshold)
                {
                    return $"Z-Score ({currentZScore:N2}) hit stop-loss (>= {_entryZScoreThreshold:N2}) - Exit Long Spread";
                }
            }
            else if (currentPosition.Direction == PositionDirection.Short)
            {
                if (currentZScore <= _exitZScoreThreshold)
                {
                    return $"Z-Score ({currentZScore:N2}) reverted to mean (<= {_exitZScoreThreshold:N2}) - Exit Short Spread";
                }
                if (currentZScore <= -_entryZScoreThreshold)
                {
                    return $"Z-Score ({currentZScore:N2}) hit stop-loss (<= -{_entryZScoreThreshold:N2}) - Exit Short Spread";
                }
            }
            return "No specific exit signal reason for pair";
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            throw new NotImplementedException();
        }

        public double GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, double maxTradeAllocation)
        {
            throw new NotImplementedException();
        }

        public double GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, double maxTradeAllocationInitialCapital, double currentTotalEquity, double kellyHalfFraction)
        {
            throw new NotImplementedException();
        }

        public long GetMinimumAverageDailyVolume()
        {
            throw new NotImplementedException();
        }
    }
}
