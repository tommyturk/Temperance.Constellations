using MathNet.Numerics.Statistics;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Utilities.Helpers;

namespace Temperance.Services.Trading.Strategies.MeanReversion.Implementations
{
    public class PairsTradingStrategy : IPairTradingStrategy
    {
        public string Name => "PairsTrading_Cointegration";

        private string _symbolA;
        private string _symbolB;
        private decimal _hedgeRatio;
        private int _spreadLookbackPeriod;
        private decimal _entryZScoreThreshold;
        private decimal _exitZScoreThreshold;

        public void Initialize(decimal initialCapital, Dictionary<string, object> parameters)
        {
            _symbolA = ParameterHelper.GetParameterOrDefault<string>(parameters, "SymbolA", string.Empty);
            _symbolB = ParameterHelper.GetParameterOrDefault<string>(parameters, "SymbolB", string.Empty);
            _hedgeRatio = ParameterHelper.GetParameterOrDefault<decimal>(parameters, "HedgeRatio", 0m);
            _spreadLookbackPeriod = ParameterHelper.GetParameterOrDefault<int>(parameters, "SpreadLookbackPeriod", 60);
            _entryZScoreThreshold = ParameterHelper.GetParameterOrDefault<decimal>(parameters, "EntryZScoreThreshold", 2.0m);
            _exitZScoreThreshold = ParameterHelper.GetParameterOrDefault<decimal>(parameters, "ExitZScoreThreshold", 0.5m);
        }

        public int GetRequiredLookbackPeriod() => _spreadLookbackPeriod;

        public SignalDecision GenerateSignal(PriceModel currentBarA,
            PriceModel currentBarB, Dictionary<string, decimal> currentIndicatorValues)
        {
            if (!currentIndicatorValues.TryGetValue("ZScore", out decimal zScore))
                return SignalDecision.Hold;

            if (zScore > _entryZScoreThreshold)
                return SignalDecision.Sell;
            else if (zScore < -_entryZScoreThreshold)
                return SignalDecision.Buy;

            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(
            Position position,
            PriceModel currentBarA,
            PriceModel currentBarB,
            Dictionary<string, decimal> currentIndicatorValues)
        {
            if (!currentIndicatorValues.TryGetValue("ZScore", out decimal zScore))
                return false;

            if (position.Direction == PositionDirection.Long && zScore >= -_exitZScoreThreshold)
                return true;
            if (position.Direction == PositionDirection.Short && zScore <= _exitZScoreThreshold)
                return true;

            return false;
        }

        private List<decimal> CalculateSpreadHistory(
        IReadOnlyList<PriceModel> dataA,
        IReadOnlyList<PriceModel> dataB)
        {
            return dataA.Zip(dataB, (a, b) => (decimal)a.ClosePrice - (_hedgeRatio * (decimal)b.ClosePrice)).ToList();
        }

        private decimal? CalculateCurrentZScore(
            PriceModel currentBarA,
            PriceModel currentBarB,
            decimal hedgeRatio,
            IReadOnlyList<PriceModel> historicalDataA,
            IReadOnlyList<PriceModel> historicalDataB)
        {
            if (historicalDataA.Count < _spreadLookbackPeriod || historicalDataB.Count < _spreadLookbackPeriod)
                return null; 

            var spreadHistory = historicalDataA
                .Zip(historicalDataB, (a, b) => (decimal)a.ClosePrice - (hedgeRatio * (decimal)b.ClosePrice))
                .Select(s => (double)s)
                .ToList<double>();

            var stats = new DescriptiveStatistics(spreadHistory);
            var movingAverage = (decimal)stats.Mean;
            var standardDeviation = (decimal)stats.StandardDeviation;

            if (standardDeviation == 0)
                return null;

            var currentSpread = (decimal)(currentBarA.ClosePrice - (hedgeRatio * currentBarB.ClosePrice));

            return (currentSpread - movingAverage) / standardDeviation;
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, PriceModel currentBar, SignalDecision exitSignal)
        {
            throw new NotImplementedException();
        }

        public decimal GetAllocationAmount(PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, decimal maxTradeAllocation)
        {
            throw new NotImplementedException();
        }

        public decimal GetAllocationAmount(PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, decimal maxTradeAllocationInitialCapital, decimal currentTotalEquity, decimal kellyHalfFraction)
        {
            throw new NotImplementedException();
        }

        public long GetMinimumAverageDailyVolume()
        {
            throw new NotImplementedException();
        }

        public void UpdateParameters(Dictionary<string, object> newParameters)
        {
            throw new NotImplementedException();
        }

        public void UpdateParameters(Dictionary<string, string> newParameters)
        {
            throw new NotImplementedException();
        }
    }
}
