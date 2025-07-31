using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Temperance.Data.Models.Trading;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class PortfolioManager : IPortfolioManager
    {
        private readonly ILogger<PortfolioManager> _logger;
        private double _initialCapital;
        private readonly object _cashLock = new object();
        private double _currentCash;
        private readonly ConcurrentDictionary<string, Position> _openPositions;
        private readonly ConcurrentBag<TradeSummary> _completedTradesHistory;
        private double _allocatedCapital;

        public PortfolioManager(ILogger<PortfolioManager> logger)
        {
            _logger = logger;
            _openPositions = new ConcurrentDictionary<string, Position>();
            _completedTradesHistory = new ConcurrentBag<TradeSummary>();
        }

        public Task Initialize(double initialCapital)
        {
            lock (_cashLock)
            {
                _currentCash = initialCapital;
            }
            _openPositions.Clear();
            _completedTradesHistory.Clear();
            _logger.LogInformation("Portfolio initialized with capital: {InitialCapital:C}", initialCapital);
            return Task.CompletedTask;
        }

        public double GetAvailableCapital()
        {
            lock (_cashLock)
            {
                return _currentCash;
            }
        }

        public double GetTotalEquity()
        {
            double openPositionValue = _openPositions.Values.Sum(p => p.EntryPrice * p.Quantity);
            return GetAvailableCapital() + openPositionValue;
        }

        public double GetAllocatedCapital() => _allocatedCapital;

        public IReadOnlyList<Position> GetOpenPositions() => _openPositions.Values.ToList();

        public IReadOnlyList<TradeSummary> GetCompletedTradesHistory() => _completedTradesHistory.ToList();

        public Task<bool> CanOpenPosition(double allocationAmount)
        {
            return Task.FromResult(GetAvailableCapital() >= allocationAmount);
        }

        public Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate, double totalEntryCost)
        {
            double totalCashOutlay = (quantity * entryPrice) + totalEntryCost;

            lock (_cashLock)
            {
                if (_currentCash < totalCashOutlay)
                {
                    _logger.LogWarning("Insufficient cash to open position for {Symbol}. Required: {Required}, Available: {Available}", symbol, totalCashOutlay, _currentCash);
                    return Task.CompletedTask;
                }
                _currentCash -= totalCashOutlay;
            }

            var newPosition = new Position
            {
                Symbol = symbol,
                Direction = direction,
                Quantity = quantity,
                EntryPrice = entryPrice,
                EntryDate = entryDate,
                TotalEntryCost = totalEntryCost
            };

            if (!_openPositions.TryAdd(symbol, newPosition))
            {
                _logger.LogError("Failed to open position for {Symbol} as one already exists.", symbol);
                lock (_cashLock) { _currentCash += totalCashOutlay; }
            }
            return Task.CompletedTask;
        }

        public async Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade)
        {
            lock (_cashLock)
            {
                double totalCashOutlay = (trade.QuantityA * trade.EntryPriceA) + (trade.QuantityB * trade.EntryPriceB)
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
        }

        public Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, double exitPrice, DateTime exitDate, double transactionCost, double profitLoss)
        {
            if (_openPositions.TryRemove(symbol, out var closedPosition))
            {
                double proceeds = (quantity * exitPrice) - transactionCost;

                lock (_cashLock)
                {
                    _currentCash += proceeds;
                }

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
                return Task.FromResult<TradeSummary?>(finalTradeSummary);
            }

            _logger.LogWarning("Attempted to close position for {Symbol} but no open position found. This might indicate a logic error or out-of-sync state.", symbol);
            return Task.FromResult<TradeSummary?>(null);
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
