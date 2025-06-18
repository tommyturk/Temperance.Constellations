using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Trading.Strategies
{
    public interface ISingleAssetStrategy : IBaseStrategy
    {
        string Name { get; }

        Dictionary<string, object> GetDefaultParameters();

        void Initialize(double initialCapital, Dictionary<string, object> parameters);

        SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalData);

        SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues);

        TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historaicalDataWindow);

        bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, Dictionary<string, double> currentIndicatorValues);
        int GetRequiredLookbackPeriod();

        double GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, double maxTradeAllocation);

        double GetAllocationAmount(
           HistoricalPriceModel currentBar,
           IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
           double maxTradeAllocationInitialCapital,
           double currentTotalEquity,
           double kellyHalfFraction);

        long GetMinimumAverageDailyVolume();

        double[] CalculateRSI(double[] prices, int period);
    }
}
    