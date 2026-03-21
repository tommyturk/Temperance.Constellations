using Newtonsoft.Json;
using System.Collections.Concurrent;
using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Trading;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Models.Trading;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Constellations;

namespace Temperance.Services.Services.Implementations
{
    public class PortfolioManager : IPortfolioManager
    {
        private readonly ILogger<PortfolioManager> _logger;
        private decimal _initialCapital;
        private readonly object _cashLock = new object();
        private decimal _currentCash;
        private readonly ConcurrentDictionary<string, Constellations.Models.Trading.Position> _openPositions;
        private readonly ConcurrentBag<TradeSummary> _completedTradesHistory;
        private decimal _allocatedCapital;
        private Guid _sessionId;

        public PortfolioManager(ILogger<PortfolioManager> logger)
        {
            _logger = logger;
            _openPositions = new ConcurrentDictionary<string, Constellations.Models.Trading.Position>();
            _completedTradesHistory = new ConcurrentBag<TradeSummary>();
        }

        public Task Initialize(Guid sessionId, decimal initialCapital)
        {
            lock (_cashLock)
            {
                _currentCash = initialCapital;
                _sessionId = sessionId;
            }
            _openPositions.Clear();
            _completedTradesHistory.Clear();
            _logger.LogInformation("Portfolio initialized with capital: {InitialCapital:C}", initialCapital);
            return Task.CompletedTask;
        }

        public decimal GetAvailableCapital()
        {
            lock (_cashLock)
            {
                return _currentCash;
            }
        }

        public decimal GetTotalEquity()
        {
            decimal openPositionValue = _openPositions.Values.Sum(p => p.AverageEntryPrice * p.Quantity);
            return GetAvailableCapital() + openPositionValue;
        }

        public decimal GetTotalEquity(Dictionary<string, decimal> latestPrices)
        {
            decimal openPositionValue = 0;
            lock (_cashLock)
            {
                foreach (var position in _openPositions.Values)
                    if (latestPrices.TryGetValue(position.Symbol, out decimal currentPrice))
                        openPositionValue += currentPrice * position.Quantity;
                    else
                        openPositionValue += position.AverageEntryPrice * position.Quantity;
            }

            return GetAvailableCapital() + openPositionValue;
        }

        public decimal GetAllocatedCapital() => _allocatedCapital;

        public IReadOnlyList<Constellations.Models.Trading.Position> GetOpenPositions() => _openPositions.Values.ToList();

        public IReadOnlyList<TradeSummary> GetCompletedTradesHistory() => _completedTradesHistory.ToList();

        public Task<bool> CanOpenPosition(decimal allocationAmount)
        {
            return Task.FromResult(GetAvailableCapital() >= allocationAmount);
        }

        public void UpdateHoldings(Dictionary<string, decimal> currentPrices)
        {
            foreach(var position in _openPositions.Values)
                if (currentPrices.TryGetValue(position.Symbol, out var currentPrice) && currentPrice > 0)
                    position.CurrentMarketValue = position.Quantity * currentPrice;
        }

        public Task<Constellations.Models.Trading.Position?> OpenPosition(
            string symbol,
            string interval,
            PositionDirection direction,
            int quantity,
            decimal entryPrice,
            DateTime entryDate,
            decimal totalEntryCost)
        {
            if (_openPositions.TryGetValue(symbol, out var existingPosition))
            {
                _logger.LogWarning("Failed to open position for {Symbol} as one already exists. Returning existing position.", symbol);
                return Task.FromResult<Constellations.Models.Trading.Position?>(existingPosition);
            }

            decimal totalCashOutlay = (quantity * entryPrice) + totalEntryCost;

            lock (_cashLock)
            {
                if (_currentCash < totalCashOutlay)
                {
                    _logger.LogWarning("Insufficient cash to open position for {Symbol}. Required: {Required:C}, Available: {Available:C}",
                        symbol, totalCashOutlay, _currentCash);
                    return Task.FromResult<Constellations.Models.Trading.Position?>(null);
                }

                _currentCash -= totalCashOutlay;
            }

            var newPosition = new Constellations.Models.Trading.Position
            {
                Symbol = symbol,
                Direction = direction,
                Quantity = quantity,
                AverageEntryPrice = entryPrice,
                InitialEntryDate = entryDate,
                TotalEntryCost = totalEntryCost
            };

            if (_openPositions.TryAdd(symbol, newPosition))
            {
                _logger.LogInformation("Successfully opened new position for {Symbol}.", symbol);
                return Task.FromResult<Constellations.Models.Trading.Position?>(newPosition);
            }
            else
            {
                lock (_cashLock) { _currentCash += totalCashOutlay; }

                if (_openPositions.TryGetValue(symbol, out var positionAfterRace))
                {
                    _logger.LogWarning("Lost concurrency race for {Symbol}. Cash rolled back. Returning winning existing position.", symbol);
                    return Task.FromResult<Constellations.Models.Trading.Position?>(positionAfterRace);
                }

                _logger.LogError("CRITICAL CONCURRENCY ERROR: Position for {Symbol} exists but could not be retrieved for state continuity.", symbol);
                return Task.FromResult<Constellations.Models.Trading.Position?>(null);
            }
        }

        public Constellations.Models.Trading.Position? GetOpenPosition(string symbol, string interval)
        {
            var openPositions = GetOpenPositions();
            if (openPositions.Count == 0)
                return null;
            return openPositions.Where(x => x.Symbol == symbol && x.Interval == interval).FirstOrDefault();
        }

        public Task AddToPosition(string symbol, int quantityToAdd, decimal entryPrice, decimal transactionCost)
        {
            if (!_openPositions.TryGetValue(symbol, out var existingPosition))
            {
                _logger.LogError("Attempted to add to a non-existent position for {Symbol}.", symbol);
                return Task.CompletedTask;
            }

            decimal additionalCashOutlay = (quantityToAdd * entryPrice) + transactionCost;
            lock (_cashLock)
            {
                if (_currentCash < additionalCashOutlay)
                {
                    _logger.LogWarning("Insufficient cash to add to position for {Symbol}", symbol);
                    return Task.CompletedTask;
                }
                _currentCash -= additionalCashOutlay;
            }

            decimal newTotalQuantity = existingPosition.Quantity + quantityToAdd;
            decimal newTotalValue = (existingPosition.AverageEntryPrice * existingPosition.Quantity) + (entryPrice * quantityToAdd);

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
                decimal totalCashOutlay = (trade.QuantityA * trade.EntryPriceA) + (trade.QuantityB * trade.EntryPriceB)
                    + trade.TotalEntryTransactionCost;

                if (_currentCash < totalCashOutlay)
                {
                    throw new InvalidOperationException($"Insufficient cash to open pair position for {pairIdentifier}. Required: {totalCashOutlay:C}, Available: {_currentCash:C}.");
                }

                _currentCash -= totalCashOutlay;

                var positionA = new Constellations.Models.Trading.Position
                {
                    Symbol = trade.SymbolA,
                    Quantity = (int)trade.QuantityA,
                    Direction = trade.Direction == PositionDirection.Long ? PositionDirection.Long : PositionDirection.Short,
                    EntryPrice = trade.EntryPriceA,
                    EntryDate = trade.EntryDate,
                };

                var positionB = new Constellations.Models.Trading.Position
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

                decimal totalValue = (positionA.EntryPrice * positionA.Quantity) + (positionB.EntryPrice * positionB.Quantity);
                _allocatedCapital += (trade.QuantityA * trade.EntryPriceA) + (trade.QuantityB * trade.EntryPriceB);
            }
        }

        public Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, 
            int quantity, decimal exitPrice, DateTime exitDate, decimal transactionCost, decimal profitLoss)
        {
            if (_openPositions.TryRemove(symbol, out var closedPosition))
            {
                decimal proceeds = (quantity * exitPrice) - transactionCost;

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

        public async Task<TradeSummary?> PartiallyClosePosition(string symbol, int quantityToClose, decimal exitPrice, DateTime exitDate, decimal transactionCost)
        {
            if (!_openPositions.TryGetValue(symbol, out var position))
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

            decimal proceeds = (quantityToClose * exitPrice) - transactionCost;

            lock (_cashLock) _currentCash += proceeds;

            decimal profitLoss = 0;
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
                decimal proceeds = completedTrade.Quantity * (completedTrade.ExitPrice ?? 0);

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
            decimal exitPriceA,
            decimal exitPriceB,
            DateTime exitTimestamp,
            decimal totalExitTransactionCost)
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

            _openPositions.Remove(positionA.Key, out Constellations.Models.Trading.Position valueA);
            _openPositions.Remove(positionB.Key, out Constellations.Models.Trading.Position valueB);

            decimal closingValueA = positionA.Value.Quantity * exitPriceA;
            decimal closingValueB = positionB.Value.Quantity * exitPriceB;
            _currentCash += closingValueA + closingValueB - totalExitTransactionCost;

            var positionAValue = positionA.Value;
            var positionBValue = positionB.Value;

            decimal pnlA = (positionAValue.Direction == PositionDirection.Long)
                ? (exitPriceA - positionAValue.EntryPrice) * positionAValue.Quantity
                : (positionAValue.EntryPrice - exitPriceA) * positionAValue.Quantity;
            decimal pnlB = (positionBValue.Direction == PositionDirection.Long)
                ? (exitPriceB - positionBValue.EntryPrice) * positionBValue.Quantity
                : (positionBValue.EntryPrice - exitPriceB) * positionBValue.Quantity;
            decimal totalTransactionCost = activeTrade.TotalEntryTransactionCost + totalExitTransactionCost;
            decimal netProfitLoss = (pnlA + pnlB) - totalTransactionCost;

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

        public void HydrateState(decimal cash, IEnumerable<Constellations.Models.Trading.Position> openPositions)
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

        public Task UpdateMarketPricesAsync(DateTime timestamp, Dictionary<string, PriceModel> currentPrices)
        {
            foreach (var position in _openPositions.Values)
            {
                if (currentPrices.TryGetValue(position.Symbol, out var bar))
                {
                    // 1. Get the current price from the bar
                    decimal currentPrice = bar.ClosePrice;

                    // 2. Update the *only* property that needs to be set.
                    // Your model calculates the rest automatically.
                    position.CurrentMarketValue = currentPrice * position.Quantity;
                }
                // If no price is found, we hold the last known value,
                // so no 'else' block is needed.
            }

            // You can optionally update the portfolio's total equity here
            // _totalEquity = _cash + _openPositions.Values.Sum(p => p.UnrealizedPnL + p.CostBasis);
            // Or, more simply, if UnrealizedPnL is correct:
            // _totalEquity = _currentCash + _openPositions.Values.Sum(p => p.CurrentMarketValue);

            return Task.CompletedTask;
        }

        public PortfolioStateModel GetPortfolioState()
        {
            return new PortfolioStateModel
            {
                SessionId = _sessionId, 
                Cash = _currentCash, 
                OpenPositionsJson = JsonConvert.SerializeObject(_openPositions.Values),
                AsOfDate = DateTime.UtcNow,
            };
        }

        public PortfolioStateModel GetPortfolioState(Dictionary<string, decimal> currentPrices)
        {
            decimal totalUnrealizedPnL = 0;
            decimal marketValueOfPositions = 0;

            foreach (var position in _openPositions.Values)
            {
                if (currentPrices.TryGetValue(position.Symbol, out var currentPrice))
                {
                    var pnl = (currentPrice - position.EntryPrice) * position.Quantity;
                    totalUnrealizedPnL += pnl;

                    marketValueOfPositions += (currentPrice * position.Quantity);
                }
                else
                {
                    marketValueOfPositions += (position.EntryPrice * position.Quantity);
                    _logger.LogWarning("No current price found for {Symbol} during state snapshot.", position.Symbol);
                }
            }

            return new PortfolioStateModel
            {
                SessionId = _sessionId,
                Cash = _currentCash,
                OpenPositionsJson = JsonConvert.SerializeObject(_openPositions.Values),
                AsOfDate = DateTime.UtcNow,

                UnrealizedPnL = totalUnrealizedPnL,

                TotalEquity = _currentCash + marketValueOfPositions
            };
        }


        public bool HasOpenPosition(string symbol)
        {
            return _openPositions.Values.Any(p => p.Symbol == symbol);
        }
    }
}
