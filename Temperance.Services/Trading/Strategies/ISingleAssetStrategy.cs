using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Trading.Strategies
{
    public interface ISingleAssetStrategy : IBaseStrategy
    {
        string Name { get; }
        Dictionary<string, object> GetDefaultParameters();
        void Initialize(double initialCapital, Dictionary<string, object> parameters);

        SignalDecision GenerateSignal(in HistoricalPriceModel currentBar, Position currentPosition, ReadOnlySpan<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues);

        TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(Position position, in HistoricalPriceModel currentBar, ReadOnlySpan<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues);

        int GetRequiredLookbackPeriod();
        long GetMinimumAverageDailyVolume();

        double GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, double maxTradeAllocation);

        double GetAllocationAmount(
            in HistoricalPriceModel currentBar,
            ReadOnlySpan<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues,
            double maxTradeAllocationInitialCapital,
            double currentTotalEquity,
            double kellyHalfFraction,
            int currentPyramidEntries);

        int GetMaxPyramidEntries();

        double[] CalculateRSI(double[] prices, int period);

        string GetExitReason(Position currentPosition, HistoricalPriceModel currentBar, List<HistoricalPriceModel> dataWindow, Dictionary<string, double> currentIndicatorValues);

        string GetEntryReason(HistoricalPriceModel currentBar, List<HistoricalPriceModel> dataWindow, Dictionary<string, double> currentIndicatorValues);

        string GetEntryReason(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues);

        string GetExitReason(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues);
    }
}
    