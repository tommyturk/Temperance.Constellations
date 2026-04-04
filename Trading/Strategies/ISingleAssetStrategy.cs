using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Services.Trading.Strategies
{
    public interface ISingleAssetStrategy : IBaseStrategy
    {
        string Name { get; }
        Dictionary<string, object> GetDefaultParameters();
        void Initialize(decimal initialCapital, Dictionary<string, object> parameters);

        decimal GetAtrMultiplier();

        decimal GetStdDevMultiplier();

        SignalDecision GenerateSignal(in PriceModel currentBar, Position currentPosition, IReadOnlyList<PriceModel> historicalDataWindow, Dictionary<string, decimal> currentIndicatorValues, MarketHealthScore marketHealth);

        TradeSummary ClosePosition(TradeSummary activeTrade, PriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(Position position, in PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, Dictionary<string, decimal> currentIndicatorValues);

        int GetRequiredLookbackPeriod();
        long GetMinimumAverageDailyVolume();

        decimal GetAllocationAmount(
            in PriceModel currentBar,
            IReadOnlyList<PriceModel> historicalDataWindow,
            Dictionary<string, decimal> currentIndicatorValues,
            decimal maxTradeAllocationInitialCapital,
            decimal currentTotalEquity,
            decimal expectedSharpe,
            int rawMacroScore,      // <-- Updated
            decimal dynamicIdm,     // <-- Updated
            int activePortfolioSize);

        int GetMaxPyramidEntries();

        decimal[] CalculateRSI(decimal[] prices, int period);

        string GetEntryReason(PriceModel currentBar, List<PriceModel> dataWindow, Dictionary<string, decimal> currentIndicatorValues);

        string GetEntryReason(
            in PriceModel currentBar,
            IReadOnlyList<PriceModel> historicalDataWindow,
            Dictionary<string, decimal> currentIndicatorValues);

        string GetExitReason(Position position, in PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, Dictionary<string, decimal> currentIndicatorValues);


        bool ShouldTakePartialProfit(Position position, in PriceModel currentBar, Dictionary<string, decimal> currentIndicatorValues);

        void UpdateParameters(Dictionary<string, object> newParameters);
    }
}
    