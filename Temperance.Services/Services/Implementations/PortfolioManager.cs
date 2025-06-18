using CsvHelper.Configuration.Attributes;
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
        private double _initialCapital;
        private double _currentCash;
        private ConcurrentDictionary<string, Position> _openPositions;
        private double _allocatedCapital;

        private readonly List<TradeSummary> _completedTradesHistory;
        public PortfolioManager(ILogger<PortfolioManager> logger)
        {
            _logger = logger;
            _openPositions = new ConcurrentDictionary<string, Position>();
            _completedTradesHistory = new List<TradeSummary>();
            _allocatedCapital = 0;
        }
        
        public Task Initialize(double initialCapital)
        {
            _initialCapital = initialCapital;
            _currentCash = initialCapital;
            _allocatedCapital = 0;
            _openPositions.Clear();
            _completedTradesHistory.Clear();
            _logger.LogInformation("Portfolio initialized with capital: {InitialCapital}", initialCapital);
            return Task.CompletedTask;
        }

        public double GetAvailableCapital() => _currentCash;
        public double GetTotalEquity() => _currentCash + _allocatedCapital;
        public double GetAllocatedCapital() => _allocatedCapital;
        public IReadOnlyList<Position> GetOpenPositions() => _openPositions.Values.ToList();
        public IReadOnlyList<TradeSummary> GetCompletedTradesHistory() => _completedTradesHistory;

        public Task<bool> CanOpenPosition(double allocationAmount)
        {
            bool canAfford = _currentCash >= allocationAmount;
            if(!canAfford) _logger.LogDebug("Insufficient funds to open position. Required: {AllocationAmount}, Available: {AvailableCash}", allocationAmount, _currentCash);

            return Task.FromResult(canAfford);
        }

        public async Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate, double transactionCost)
        {
            double totalCashOutlay = (quantity * entryPrice) + transactionCost;

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

        public async Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade)
        {
            double totalCashOutlay = (trade.QuantityA * trade.EntryPriceA ) + (trade.QuantityB * trade.EntryPriceB)
                + trade.TotalEntryTransactionCost;

            if (_currentCash < totalCashOutlay)
            {
                throw new InvalidOperationException($"Insufficient cash to open pair position for {pairIdentifier}. Required: {totalCashOutlay:C}, Available: {_currentCash:C}.");
            }

            _currentCash -= totalCashOutlay;

            var positionA = new Position
            {
                Symbol = trade.SymbolA,
                Quantity = (int)trade.QuantityA,
                Direction = trade.Direction == PositionDirection.Long ? PositionDirection.Long : PositionDirection.Short,
                EntryPrice = trade.EntryPriceA,
                EntryDate = trade.EntryDate,
            };

            var positionB = new Position
            {
                Symbol = trade.SymbolB,
                Quantity = (int)trade.QuantityB,
                Direction = trade.Direction == PositionDirection.Long ? PositionDirection.Short : PositionDirection.Long,
                EntryPrice = trade.EntryPriceB,
                EntryDate = trade.EntryDate,
            };

            _openPositions.AddOrUpdate(positionA.Symbol, positionA, (key, existingVal) =>
            {
                return positionA;
            });
            _openPositions.AddOrUpdate(positionB.Symbol, positionB, (key, existingVal) =>
            {
                return positionB;
            });

            double totalValue = (positionA.EntryPrice * positionA.Quantity) + (positionB.EntryPrice * positionB.Quantity);
            _allocatedCapital += (trade.QuantityA * trade.EntryPriceA) + (trade.QuantityB * trade.EntryPriceB);
        }

        public async Task ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, double exitPrice, DateTime exitDate, double transactionCost, double profitLoss)
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

        public Task<TradeSummary?> ClosePairPosition(
        ActivePairTrade activeTrade,
        double exitPriceA,
        double exitPriceB,
        DateTime exitTimestamp,
        double totalExitTransactionCost)
        {
            var positionA = _openPositions.FirstOrDefault(p =>
                p.Value.Symbol == activeTrade.SymbolA && p.Value.EntryDate == activeTrade.EntryDate);

            var positionB = _openPositions.FirstOrDefault(p =>
                p.Value.Symbol == activeTrade.SymbolB && p.Value.EntryDate == activeTrade.EntryDate);

            if (positionA.Value == null || positionB.Value == null)
            {
                _logger.LogError("Could not find one or both legs of the pair trade for {SymbolA}/{SymbolB} entered at {EntryDate} to close.",
                    activeTrade.SymbolA, activeTrade.SymbolB, activeTrade.EntryDate);
                return Task.FromResult<TradeSummary?>(null);
            }

            // 2. Remove the positions from the open list.
            _openPositions.Remove(positionA.Key, out Position valueA);
            _openPositions.Remove(positionB.Key, out Position valueB);

            // 3. Update cash balance by adding back the value of the closing positions.
            double closingValueA = positionA.Value.Quantity * exitPriceA;
            double closingValueB = positionB.Value.Quantity * exitPriceB;
            _currentCash += closingValueA + closingValueB - totalExitTransactionCost;

            var positionAValue = positionA.Value;
            var positionBValue = positionB.Value;

            // 4. Calculate P&L internally to ensure data integrity. This should match the runner's calculation.
            double pnlA = (positionAValue.Direction == PositionDirection.Long) 
                ? (exitPriceA - positionAValue.EntryPrice) * positionAValue.Quantity 
                : (positionAValue.EntryPrice - exitPriceA) * positionAValue.Quantity;
            double pnlB = (positionBValue.Direction == PositionDirection.Long) 
                ? (exitPriceB - positionBValue.EntryPrice) * positionBValue.Quantity 
                : (positionBValue.EntryPrice - exitPriceB) * positionBValue.Quantity;
            double totalTransactionCost = activeTrade.TotalEntryTransactionCost + totalExitTransactionCost;
            double netProfitLoss = (pnlA + pnlB) - totalTransactionCost;

            // 5. Create a single TradeSummary object to represent the aggregate result of the pair trade.
            var summary = new TradeSummary
            {
                Symbol = $"{activeTrade.SymbolA}/{activeTrade.SymbolB}", // Use a pair identifier
                Direction = activeTrade.Direction.ToString(), // "Long" or "Short" the spread
                EntryDate = activeTrade.EntryDate,
                ExitDate = exitTimestamp,
                ProfitLoss = netProfitLoss,
                TransactionCost = totalTransactionCost,
                Quantity = positionAValue.Quantity + positionBValue.Quantity, // Total quantity of both legs
            };

            // 6. Add to completed trades and return.
            _completedTradesHistory.Add(summary);

            return Task.FromResult<TradeSummary?>(summary);
        }
    }
}
