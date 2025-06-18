using Temperance.Data.Models.Trading;

namespace Temperance.Services.Services.Interfaces
{
    public interface IPortfolioManager
    {
        Task Initialize(double initialCapital);
        double GetAvailableCapital();
        double GetTotalEquity();
        double GetAllocatedCapital();
        IReadOnlyList<Position> GetOpenPositions();

        IReadOnlyList<TradeSummary> GetCompletedTradesHistory();

        Task<bool> CanOpenPosition(double allocationAmount);

        Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate, double transactionCost);

        Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade);

        Task ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, double exitPrice, DateTime exitDate, double transactionCost, double profitLoss);
        Task<TradeSummary?> ClosePairPosition(
        ActivePairTrade activeTrade,
        double exitPriceA,
        double exitPriceB,
        DateTime exitTimestamp,
        double totalExitTransactionCost);
    }
}
