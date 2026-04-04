using System.Collections.Generic;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Models.Trading; // Ensures ActivePairTrade is recognized

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IPortfolioManager
    {
        Task Initialize(Guid sessionId, decimal initialCapital);
        void HydrateState(decimal cash, IEnumerable<Models.Trading.Position> openPositions);
        
        decimal GetTotalEquity();
        decimal GetTotalEquity(Dictionary<string, decimal> latestPrices);
        decimal GetAvailableCapital();
        decimal GetAllocatedCapital();
        
        IReadOnlyList<Models.Trading.Position> GetOpenPositions();
        Models.Trading.Position? GetOpenPosition(string symbol, string interval);
        IReadOnlyList<TradeSummary> GetCompletedTradesHistory();
        bool HasOpenPosition(string symbol);

        Task<bool> CanOpenPosition(decimal allocationAmount);

        Task<Models.Trading.Position?> OpenPosition(
            string symbol,
            string interval,
            PositionDirection direction,
            int quantity,
            decimal entryPrice,
            DateTime entryDate,
            decimal totalEntryCost);

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

        void UpdateHoldings(Dictionary<string, decimal> currentPrices);

        /// <summary>
        /// Updates the Mark-to-Market (MTM) value of all open positions.
        /// </summary>
        Task UpdateMarketPricesAsync(DateTime timestamp, Dictionary<string, PriceModel> currentPrices);

        /// <summary>
        /// Gets a snapshot of the portfolio's current cash and open positions.
        /// </summary>
        PortfolioStateModel GetPortfolioState();
        PortfolioStateModel GetPortfolioState(Dictionary<string, decimal> currentPrices);
        PortfolioStateModel GetPortfolioState(Dictionary<string, PriceModel> currentPrices);
    }
}