using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Trading.Strategies
{
    public interface IPairTradingStrategy : IBaseStrategy
    {
        int GetRequiredLookbackPeriod();

        SignalDecision GenerateSignal(HistoricalPriceModel currentBarA,
            HistoricalPriceModel currentBarB, Dictionary<string, double> currentIndicatorValues);

        TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(
            Position position,
            HistoricalPriceModel currentBarA,
            HistoricalPriceModel currentBarB,
            Dictionary<string, double> currentIndicatorValues);

        double GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, double maxTradeAllocation);

        double GetAllocationAmount(
           HistoricalPriceModel currentBar,
           IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
           double maxTradeAllocationInitialCapital,
           double currentTotalEquity,
           double kellyHalfFraction);

        long GetMinimumAverageDailyVolume();
    }
}
