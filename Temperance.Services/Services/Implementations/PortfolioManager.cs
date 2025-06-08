using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Implementations;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class PortfolioManager : IPortfolioManager
    {
        private readonly ILogger<PortfolioManager> _logger;
        private decimal _initialCapital;
        private decimal _currentCash;
        private ConcurrentDictionary<string, Position> _openPositions;
        private decimal _allocatedCapital;

        private readonly List<TradeSummary> _completedTradesHistory;
        public PortfolioManager(ILogger<PortfolioManager> logger)
        {
            _logger = logger;
            _openPositions = new ConcurrentDictionary<string, Position>();
            _completedTradesHistory = new List<TradeSummary>();
            _allocatedCapital = 0;
        }
        
        public Task Initialize(decimal initialCapital)
        {
            _initialCapital = initialCapital;
            _currentCash = initialCapital;
            _allocatedCapital = 0;
            _openPositions.Clear();
            _completedTradesHistory.Clear();
            _logger.LogInformation("Portfolio initialized with capital: {InitialCapital}", initialCapital);
            return Task.CompletedTask;
        }

        public decimal GetAvailableCapital() => _currentCash;
        public decimal GetTotalEquity() => _currentCash + _allocatedCapital;
        public decimal GetAllocatedCapital() => _allocatedCapital;
        public IReadOnlyList<Position> GetOpenPositions() => _openPositions.Values.ToList();

        public IReadOnlyList<TradeSummary> GetCompletedTradesHistory() => _completedTradesHistory;


        public Task<bool> CanOpenPosition(decimal allocationAmount)
        {
            bool canAfford = _currentCash >= allocationAmount;
            if(!canAfford) _logger.LogDebug("Insufficient funds to open position. Required: {AllocationAmount}, Available: {AvailableCash}", allocationAmount, _currentCash);

            return Task.FromResult(canAfford);
        }

        public async Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, decimal entryPrice, DateTime entryDate, decimal transactionCost)
        {
            decimal totalCashOutlay = (quantity * entryPrice) + transactionCost;

            if (_currentCash < totalCashOutlay)
            {
                _logger.LogError("Attempted to open position for {Symbol} but insufficient cash in PortfolioManager. This indicates a prior `CanOpenPosition` check failed or was not called. Cash: {Cash:C}, Cost: {Cost:C}", symbol, _currentCash, totalCashOutlay);
                throw new InvalidOperationException("Insufficient cash to open position. This should have been pre-checked.");
            }

            _currentCash -= totalCashOutlay;

            var newPosition = new Position
            {
                Symbol = symbol,
                Direction = direction,
                Quantity = quantity,
                EntryPrice = entryPrice,
                EntryDate = entryDate
            };

            _openPositions.AddOrUpdate(symbol, newPosition, (key, existingVal) =>
            {
                _logger.LogWarning("Overwriting existing open position for {Symbol}. This should generally not happen in a single-position-per-symbol strategy.", symbol);
                return newPosition;
            });

            _allocatedCapital += (quantity * entryPrice);

            _logger.LogInformation("Opened {Direction} position for {Quantity} shares of {Symbol} at {EntryPrice:C}. Cash: {Cash:C}, Allocated: {Allocated:C}",
                direction, quantity, symbol, entryPrice, _currentCash, _allocatedCapital);
        }

        public async Task ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, decimal exitPrice, DateTime exitDate, decimal transactionCost, decimal profitLoss)
        {
            if (_openPositions.TryRemove(symbol, out var closedPosition))
            {
                _allocatedCapital -= (closedPosition.Quantity * closedPosition.EntryPrice);

                _currentCash += profitLoss;

                var finalTradeSummary = new TradeSummary
                {
                    StrategyName = strategyName,
                    EntryDate = closedPosition.EntryDate,
                    EntryPrice = closedPosition.EntryPrice,
                    ExitDate = exitDate,
                    ExitPrice = exitPrice,
                    Direction = direction == PositionDirection.Long ? "Long" : "Short",
                    Quantity = quantity,
                    ProfitLoss = profitLoss,
                    Symbol = symbol,
                    Interval = interval,
                    TransactionCost = transactionCost
                };

                _completedTradesHistory.Add(finalTradeSummary);

                _logger.LogInformation("Closed {Direction} position for {Quantity} shares of {Symbol}. Net PnL: {PnL:C}. Total Tx Cost: {TxCost:C}. Current Cash: {Cash:C}, Allocated: {Allocated:C}",
                    direction, quantity, symbol, profitLoss, transactionCost, _currentCash, _allocatedCapital);
            }
            else
            {
                _logger.LogWarning("Attempted to close position for {Symbol} but no open position found. This might indicate a logic error or out-of-sync state.", symbol);
            }
        }
    }
}
