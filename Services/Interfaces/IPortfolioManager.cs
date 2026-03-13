using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IPortfolioManager
    {
        Task Initialize(Guid SessionId, decimal initialCapital);
        void HydrateState(decimal cash, IEnumerable<Position> openPositions);
        decimal GetTotalEquity();
        decimal GetTotalEquity(Dictionary<string, decimal> latestPrices);
        decimal GetAvailableCapital();
        decimal GetAllocatedCapital();
        IReadOnlyList<Position> GetOpenPositions();
        Position? GetOpenPosition(string symbol, string interval);
        void UpdateHoldings(Dictionary<string, decimal> currentPrices);
        IReadOnlyList<TradeSummary> GetCompletedTradesHistory();

        Task<bool> CanOpenPosition(decimal allocationAmount);
        Task<Position?> OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, decimal entryPrice, DateTime entryDate, decimal transactionCost);
        Task AddToPosition(string symbol, int quantityToAdd, decimal entryPrice, decimal transactionCost);

        Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade);

        Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, decimal exitPrice, DateTime exitDate, decimal transactionCost, decimal profitLoss);
        Task<TradeSummary?> PartiallyClosePosition(string symbol, int quantityToClose, decimal exitPrice, DateTime exitDate, decimal transactionCost);
        Task<TradeSummary?> ClosePosition(TradeSummary completedTrade);
        Task<TradeSummary?> ClosePairPosition(
        ActivePairTrade activeTrade,
        decimal exitPriceA,
        decimal exitPriceB,
        DateTime exitTimestamp,
        decimal totalExitTransactionCost);

        /// <summary>
        /// Updates the Mark-to-Market (MTM) value of all open positions.
        /// </summary>
        Task UpdateMarketPricesAsync(DateTime timestamp, Dictionary<string, PriceModel> currentPrices);

        /// <summary>
        /// Gets a snapshot of the portfolio's current cash and open positions.
        /// </summary>
        PortfolioState GetPortfolioState();
    }
}
