using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.MarketHealth;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Trading.Strategies
{
    public interface ISingleAssetStrategy : IBaseStrategy
    {
        string Name { get; }
        Dictionary<string, object> GetDefaultParameters();
        void Initialize(double initialCapital, Dictionary<string, object> parameters);

        double GetAtrMultiplier();

        double GetStdDevMultiplier();

        SignalDecision GenerateSignal(in HistoricalPriceModel currentBar, Position currentPosition, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues, MarketHealthScore marketHealth);

        TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(Position position, in HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues);

        int GetRequiredLookbackPeriod();
        long GetMinimumAverageDailyVolume();

        double GetAllocationAmount(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues,
            double maxTradeAllocationInitialCapital,
            double currentTotalEquity,
            double kellyHalfFraction,
            int currentPyramidEntries,
            MarketHealthScore marketHealth);

        int GetMaxPyramidEntries();

        double[] CalculateRSI(double[] prices, int period);

        string GetEntryReason(HistoricalPriceModel currentBar, List<HistoricalPriceModel> dataWindow, Dictionary<string, double> currentIndicatorValues);

        string GetEntryReason(
            in HistoricalPriceModel currentBar,
            IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
            Dictionary<string, double> currentIndicatorValues);

        string GetExitReason(Position position, in HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues);


        bool ShouldTakePartialProfit(Position position, in HistoricalPriceModel currentBar, Dictionary<string, double> currentIndicatorValues);
    }
}
    