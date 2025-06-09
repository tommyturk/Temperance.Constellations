using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Trading.Strategies
{
    public interface IPairTradingStrategy : IBaseStrategy
    {
        int GetRequiredLookbackPeriod();
        SignalDecision GenerateSignal(HistoricalPriceModel currentBarA, HistoricalPriceModel currentBarB, IReadOnlyList<HistoricalPriceModel> historicalDataA,
                                      IReadOnlyList<HistoricalPriceModel> historicalDataB);
        TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historaicalDataWindow);

        decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocation);

        decimal GetAllocationAmount(
           HistoricalPriceModel currentBar,
           IReadOnlyList<HistoricalPriceModel> historicalDataWindow,
           decimal maxTradeAllocationInitialCapital,
           decimal currentTotalEquity,
           decimal kellyHalfFraction);

        long GetMinimumAverageDailyVolume();
    }
}
