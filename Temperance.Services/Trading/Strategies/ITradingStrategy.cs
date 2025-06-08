using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Trading.Strategies
{
    public interface ITradingStrategy
    {
        string Name { get; }

        Dictionary<string, object> GetDefaultParameters();

        void Initialize(decimal initialCapital, Dictionary<string, object> parameters);

        SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalData);

        TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal);

        bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historaicalDataWindow);

        int GetRequiredLookbackPeriod();

        decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocation);
    }
}
    