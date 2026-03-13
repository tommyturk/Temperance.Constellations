using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Services.Trading.Strategies
{
    public interface IPairTradingStrategy : IBaseStrategy
    {
        int GetRequiredLookbackPeriod();

        SignalDecision GenerateSignal(PriceModel currentBarA,
            PriceModel currentBarB, Dictionary<string, decimal> currentIndicatorValues);

        TradeSummary ClosePosition(TradeSummary activeTrade, PriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(
            Position position,
            PriceModel currentBarA,
            PriceModel currentBarB,
            Dictionary<string, decimal> currentIndicatorValues);

        decimal GetAllocationAmount(PriceModel currentBar, IReadOnlyList<PriceModel> historicalDataWindow, decimal maxTradeAllocation);

        decimal GetAllocationAmount(
           PriceModel currentBar,
           IReadOnlyList<PriceModel> historicalDataWindow,
           decimal maxTradeAllocationInitialCapital,
           decimal currentTotalEquity,
           decimal kellyHalfFraction);

        long GetMinimumAverageDailyVolume();
    }
}
