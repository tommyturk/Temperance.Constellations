using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Trading;

namespace Temperance.Services.Services.Interfaces
{
    public interface IPortfolioManager
    {
        Task Initialize(Guid SessionId, double initialCapital);
        void HydrateState(double cash, IEnumerable<Position> openPositions);
        double GetTotalEquity();
        double GetTotalEquity(Dictionary<string, double> latestPrices);
        double GetAvailableCapital();
        double GetAllocatedCapital();
        IReadOnlyList<Position> GetOpenPositions();
        Position? GetOpenPosition(string symbol, string interval);
        void UpdateHoldings(Dictionary<string, double> currentPrices);
        IReadOnlyList<TradeSummary> GetCompletedTradesHistory();

        Task<bool> CanOpenPosition(double allocationAmount);
        Task<Position?> OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate, double transactionCost);
        Task AddToPosition(string symbol, int quantityToAdd, double entryPrice, double transactionCost);

        Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade);

        Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, double exitPrice, DateTime exitDate, double transactionCost, double profitLoss);
        Task<TradeSummary?> PartiallyClosePosition(string symbol, int quantityToClose, double exitPrice, DateTime exitDate, double transactionCost);
        Task<TradeSummary?> ClosePosition(TradeSummary completedTrade);
        Task<TradeSummary?> ClosePairPosition(
        ActivePairTrade activeTrade,
        double exitPriceA,
        double exitPriceB,
        DateTime exitTimestamp,
        double totalExitTransactionCost);

        /// <summary>
        /// Updates the Mark-to-Market (MTM) value of all open positions.
        /// </summary>
        Task UpdateMarketPricesAsync(DateTime timestamp, Dictionary<string, HistoricalPriceModel> currentPrices);

        /// <summary>
        /// Gets a snapshot of the portfolio's current cash and open positions.
        /// </summary>
        PortfolioState GetPortfolioState();
    }
}
