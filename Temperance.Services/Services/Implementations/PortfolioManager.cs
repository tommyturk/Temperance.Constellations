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

        public double GetTotalEquity(Dictionary<string, double> latestPrices)
        {
            double openPositionValue = 0;
            lock (_cashLock) 
            {
                foreach (var position in _openPositions.Values)
                    if (latestPrices.TryGetValue(position.Symbol, out double currentPrice))
                        openPositionValue += currentPrice * position.Quantity;
                    else
                        openPositionValue += position.AverageEntryPrice * position.Quantity;
            }

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
                AverageEntryPrice = entryPrice, // Use AverageEntryPrice
                InitialEntryDate = entryDate,   // Use InitialEntryDate
                TotalEntryCost = totalEntryCost
            };

            if (!_openPositions.TryAdd(symbol, newPosition))
            {
                _logger.LogError("Failed to open position for {Symbol} as one already exists.", symbol);
                lock (_cashLock) { _currentCash += totalCashOutlay; }
            }
            return Task.CompletedTask;
        }

        public Task AddToPosition(string symbol, int quantityToAdd, double entryPrice, double transactionCost)
        {
            if (!_openPositions.TryGetValue(symbol, out var existingPosition))
            {
                _logger.LogError("Attempted to add to a non-existent position for {Symbol}.", symbol);
                return Task.CompletedTask;
            }

            double additionalCashOutlay = (quantityToAdd * entryPrice) + transactionCost;
            lock (_cashLock)
            {
                if (_currentCash < additionalCashOutlay)
                {
                    _logger.LogWarning("Insufficient cash to add to position for {Symbol}", symbol);
                    return Task.CompletedTask;
                }
                _currentCash -= additionalCashOutlay;
            }

            double newTotalQuantity = existingPosition.Quantity + quantityToAdd;
            double newTotalValue = (existingPosition.AverageEntryPrice * existingPosition.Quantity) + (entryPrice * quantityToAdd);

            existingPosition.AverageEntryPrice = newTotalValue / newTotalQuantity;
            existingPosition.Quantity = (int)newTotalQuantity;
            existingPosition.TotalEntryCost += transactionCost;
            existingPosition.PyramidEntries++;

            _logger.LogInformation("Added {Quantity} shares to {Symbol}. New Avg Price: {AvgPrice}, New Total Quantity: {TotalQuantity}",
                quantityToAdd, symbol, existingPosition.AverageEntryPrice, existingPosition.Quantity);

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

        public async Task<TradeSummary?> PartiallyClosePosition(string symbol, int quantityToClose, double exitPrice, DateTime exitDate, double transactionCost)
        {
            if(!_openPositions.TryGetValue(symbol, out var position))
            {
                _logger.LogInformation("Attempted to partially close non-existent position for {Symbol}", symbol);
                return null;
            }

            if (quantityToClose <= 0 || quantityToClose >= position.Quantity)
            {
                _logger.LogError("Invalid quantity for partial close on {Symbol}. Qty: {Qty}", symbol, quantityToClose);
                return null;
            }

            _logger.LogInformation("Partially closing {Qty} shares of {Symbol} at {Price}", quantityToClose, symbol, exitPrice);

            double proceeds = (quantityToClose * exitPrice) - transactionCost;
            
            lock (_cashLock) _currentCash += proceeds;

            double profitLoss = 0;
            if (position.Direction == PositionDirection.Long)
                profitLoss = (exitPrice - position.AverageEntryPrice) * quantityToClose;
            else
                profitLoss = (position.AverageEntryPrice - exitPrice) * quantityToClose;

            position.Quantity -= quantityToClose;

            var partialTradeSummary = new TradeSummary()
            {
                Id = Guid.NewGuid(),
                Symbol = position.Symbol,
                Direction = position.Direction.ToString(),
                EntryDate = position.InitialEntryDate,
                EntryPrice = position.AverageEntryPrice,
                ExitDate = exitDate,
                ExitPrice = exitPrice,
                Quantity = quantityToClose,
                ProfitLoss = profitLoss - transactionCost,
                TotalTransactionCost = transactionCost,
                ExitReason = "Partial Profit Target Hit"
            };

            _completedTradesHistory.Add(partialTradeSummary);
            return partialTradeSummary;
        }

        public Task<TradeSummary?> ClosePosition(TradeSummary completedTrade)
        {
            if (_openPositions.TryRemove(completedTrade.Symbol, out var closedPosition))
            {
                double proceeds = completedTrade.Quantity * (completedTrade.ExitPrice ?? 0);

                lock (_cashLock)
                {
                    _currentCash += proceeds;
                }

                _completedTradesHistory.Add(completedTrade);
                return Task.FromResult<TradeSummary?>(completedTrade);
            }

            _logger.LogWarning("Attempted to close position for {Symbol} but no open position found.", completedTrade.Symbol);
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

            _openPositions.Remove(positionA.Key, out Position valueA);
            _openPositions.Remove(positionB.Key, out Position valueB);

            double closingValueA = positionA.Value.Quantity * exitPriceA;
            double closingValueB = positionB.Value.Quantity * exitPriceB;
            _currentCash += closingValueA + closingValueB - totalExitTransactionCost;

            var positionAValue = positionA.Value;
            var positionBValue = positionB.Value;

            double pnlA = (positionAValue.Direction == PositionDirection.Long)
                ? (exitPriceA - positionAValue.EntryPrice) * positionAValue.Quantity
                : (positionAValue.EntryPrice - exitPriceA) * positionAValue.Quantity;
            double pnlB = (positionBValue.Direction == PositionDirection.Long)
                ? (exitPriceB - positionBValue.EntryPrice) * positionBValue.Quantity
                : (positionBValue.EntryPrice - exitPriceB) * positionBValue.Quantity;
            double totalTransactionCost = activeTrade.TotalEntryTransactionCost + totalExitTransactionCost;
            double netProfitLoss = (pnlA + pnlB) - totalTransactionCost;

            var summary = new TradeSummary
            {
                Symbol = $"{activeTrade.SymbolA}/{activeTrade.SymbolB}", 
                Direction = activeTrade.Direction.ToString(),
                EntryDate = activeTrade.EntryDate,
                ExitDate = exitTimestamp,
                ProfitLoss = netProfitLoss,
                TransactionCost = totalTransactionCost,
                Quantity = positionAValue.Quantity + positionBValue.Quantity,
            };

            _completedTradesHistory.Add(summary);

            return Task.FromResult<TradeSummary?>(summary);
        }

        public void HydrateState(double cash, IEnumerable<Position> openPositions)
        {
            lock (_cashLock)
            {
                _currentCash = cash;
            }
            _openPositions.Clear();
            foreach (var position in openPositions)
            {
                _openPositions.TryAdd(position.Symbol, position);
            }
            _completedTradesHistory.Clear(); 
            _logger.LogInformation("PortfolioManager state hydrated with {Cash:C} cash and {PositionCount} open positions.", cash, openPositions.Count());
        }
    }
}
