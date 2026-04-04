using Newtonsoft.Json;
using System.Collections.Generic;
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

        // --- LOCKS ---
        private readonly object _cashLock = new object();
        private readonly object _positionLock = new object();

        private decimal _currentCash;
        private Guid _sessionId;

        private readonly ConcurrentDictionary<string, Constellations.Models.Trading.Position> _openPositions;
        private readonly ConcurrentBag<TradeSummary> _completedTradesHistory;

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
            decimal openMarketValue = 0;
            lock (_positionLock)
            {
                openMarketValue = _openPositions.Values.Sum(p => p.CurrentMarketValue);
            }
            return GetAvailableCapital() + openMarketValue;
        }

        public decimal GetTotalEquity(Dictionary<string, decimal> latestPrices)
        {
            decimal openPositionValue = 0;
            lock (_positionLock)
            {
                foreach (var position in _openPositions.Values)
                {
                    if (latestPrices.TryGetValue(position.Symbol, out decimal currentPrice))
                        openPositionValue += currentPrice * position.Quantity;
                    else
                        openPositionValue += position.AverageEntryPrice * position.Quantity;
                }
            }
            return GetAvailableCapital() + openPositionValue;
        }

        public decimal GetAllocatedCapital()
        {
            lock (_positionLock)
            {
                return _openPositions.Values.Sum(p => p.AverageEntryPrice * p.Quantity);
            }
        }

        public IReadOnlyList<Constellations.Models.Trading.Position> GetOpenPositions() => _openPositions.Values.ToList();

        public IReadOnlyList<TradeSummary> GetCompletedTradesHistory() => _completedTradesHistory.ToList();

        public Task<bool> CanOpenPosition(decimal allocationAmount)
        {
            return Task.FromResult(GetAvailableCapital() >= allocationAmount);
        }

        public void UpdateHoldings(Dictionary<string, decimal> currentPrices)
        {
            lock (_positionLock)
            {
                foreach (var position in _openPositions.Values)
                {
                    if (currentPrices.TryGetValue(position.Symbol, out var currentPrice) && currentPrice > 0)
                        position.CurrentMarketValue = position.Quantity * currentPrice;
                }
            }
        }

        public Task UpdateMarketPricesAsync(DateTime timestamp, Dictionary<string, PriceModel> currentPrices)
        {
            lock (_positionLock)
            {
                foreach (var position in _openPositions.Values)
                {
                    if (currentPrices.TryGetValue(position.Symbol, out var bar))
                    {
                        decimal currentPrice = bar.ClosePrice;

                        if (position.Direction == PositionDirection.Long)
                        {
                            position.CurrentMarketValue = currentPrice * position.Quantity;
                        }
                        else // SHORT POSITION
                        {
                            decimal marginHeld = position.AverageEntryPrice * position.Quantity;
                            decimal unrealizedPnL = (position.AverageEntryPrice - currentPrice) * position.Quantity;
                            position.CurrentMarketValue = marginHeld + unrealizedPnL;
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }

        public Task<Constellations.Models.Trading.Position?> OpenPosition(
            string symbol, string interval, PositionDirection direction,
            int quantity, decimal entryPrice, DateTime entryDate, decimal totalEntryCost)
        {
            lock (_cashLock)
            {
                if (_openPositions.TryGetValue(symbol, out var existingPosition))
                {
                    _logger.LogWarning("Failed to open position for {Symbol} as one already exists.", symbol);
                    return Task.FromResult<Constellations.Models.Trading.Position?>(existingPosition);
                }

                decimal totalCashOutlay = (quantity * entryPrice) + totalEntryCost;
                if (_currentCash < totalCashOutlay)
                {
                    return Task.FromResult<Constellations.Models.Trading.Position?>(null);
                }

                _currentCash -= totalCashOutlay;

                var newPosition = new Constellations.Models.Trading.Position
                {
                    Symbol = symbol,
                    Interval = interval,
                    Direction = direction,
                    Quantity = quantity,
                    AverageEntryPrice = entryPrice,
                    TotalEntryCost = totalEntryCost,
                    InitialEntryDate = entryDate,
                    HighestPriceSinceEntry = entryPrice,
                    LowestPriceSinceEntry = entryPrice,
                    CurrentMarketValue = quantity * entryPrice
                };

                _openPositions.TryAdd(symbol, newPosition);

                return Task.FromResult<Constellations.Models.Trading.Position?>(newPosition);
            }
        }

        public Constellations.Models.Trading.Position? GetOpenPosition(string symbol, string interval)
        {
            if (_openPositions.TryGetValue(symbol, out var pos) && pos.Interval == interval)
                return pos;
            return null;
        }

        public Task AddToPosition(string symbol, int quantityToAdd, decimal entryPrice, decimal transactionCost)
        {
            if (!_openPositions.TryGetValue(symbol, out var existingPosition)) return Task.CompletedTask;

            decimal additionalCashOutlay = (quantityToAdd * entryPrice) + transactionCost;

            lock (_cashLock)
            {
                if (_currentCash < additionalCashOutlay) return Task.CompletedTask;
                _currentCash -= additionalCashOutlay;
            }

            lock (_positionLock)
            {
                decimal newTotalQuantity = existingPosition.Quantity + quantityToAdd;
                decimal newTotalValue = (existingPosition.AverageEntryPrice * existingPosition.Quantity) + (entryPrice * quantityToAdd);

                existingPosition.AverageEntryPrice = newTotalValue / newTotalQuantity;
                existingPosition.Quantity = (int)newTotalQuantity;
                existingPosition.TotalEntryCost += transactionCost;
                existingPosition.PyramidEntries++;
            }

            return Task.CompletedTask;
        }

        public Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade)
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
                    AverageEntryPrice = trade.EntryPriceA, // Added to prevent MTM bugs
                    EntryDate = trade.EntryDate,
                    InitialEntryDate = trade.EntryDate
                };

                var positionB = new Constellations.Models.Trading.Position
                {
                    Symbol = trade.SymbolB,
                    Quantity = (int)trade.QuantityB,
                    Direction = trade.Direction == PositionDirection.Long ? PositionDirection.Short : PositionDirection.Long,
                    EntryPrice = trade.EntryPriceB,
                    AverageEntryPrice = trade.EntryPriceB, // Added to prevent MTM bugs
                    EntryDate = trade.EntryDate,
                    InitialEntryDate = trade.EntryDate
                };

                _openPositions.AddOrUpdate(positionA.Symbol, positionA, (key, existingVal) => positionA);
                _openPositions.AddOrUpdate(positionB.Symbol, positionB, (key, existingVal) => positionB);
            }
            return Task.CompletedTask;
        }

        public Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction,
            int quantity, decimal exitPrice, DateTime exitDate, decimal transactionCost, decimal profitLoss)
        {
            if (_openPositions.TryRemove(symbol, out var closedPosition))
            {
                decimal marginReleased = quantity * closedPosition.AverageEntryPrice;
                decimal entryFeesReturned = closedPosition.TotalEntryCost;
                decimal proceeds = marginReleased + entryFeesReturned + profitLoss;

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

            return Task.FromResult<TradeSummary?>(null);
        }

        public async Task<TradeSummary?> PartiallyClosePosition(string symbol, int quantityToClose, decimal exitPrice, DateTime exitDate, decimal transactionCost)
        {
            if (!_openPositions.TryGetValue(symbol, out var position)) return null;

            lock (_positionLock)
            {
                if (quantityToClose <= 0 || quantityToClose >= position.Quantity) return null;

                decimal proportion = (decimal)quantityToClose / position.Quantity;
                decimal allocatedEntryCost = position.TotalEntryCost * proportion;
                decimal allocatedMargin = position.AverageEntryPrice * quantityToClose;

                decimal grossPnL = position.Direction == PositionDirection.Long
                     ? (exitPrice - position.AverageEntryPrice) * quantityToClose
                     : (position.AverageEntryPrice - exitPrice) * quantityToClose;

                decimal netPnL = grossPnL - transactionCost - allocatedEntryCost;
                decimal proceeds = allocatedMargin + allocatedEntryCost + netPnL;

                lock (_cashLock)
                {
                    _currentCash += proceeds;
                }

                position.Quantity -= quantityToClose;
                position.TotalEntryCost -= allocatedEntryCost;

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
                    ProfitLoss = netPnL,
                    TotalTransactionCost = transactionCost + allocatedEntryCost,
                    ExitReason = "Partial Profit Target Hit"
                };

                _completedTradesHistory.Add(partialTradeSummary);
                return partialTradeSummary;
            }
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
                _logger.LogError("Could not find one or both legs of the pair trade to close.");
                return Task.FromResult<TradeSummary?>(null);
            }

            _openPositions.Remove(positionA.Key, out Constellations.Models.Trading.Position valueA);
            _openPositions.Remove(positionB.Key, out Constellations.Models.Trading.Position valueB);

            decimal closingValueA = positionA.Value.Quantity * exitPriceA;
            decimal closingValueB = positionB.Value.Quantity * exitPriceB;

            lock (_cashLock)
            {
                _currentCash += closingValueA + closingValueB - totalExitTransactionCost;
            }

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
            decimal totalMarginHeld = 0;

            lock (_positionLock)
            {
                foreach (var position in _openPositions.Values)
                {
                    decimal currentPrice = currentPrices.TryGetValue(position.Symbol, out var price) ? price : position.AverageEntryPrice;

                    decimal pnl = position.Direction == PositionDirection.Long
                        ? (currentPrice - position.AverageEntryPrice) * position.Quantity
                        : (position.AverageEntryPrice - currentPrice) * position.Quantity;

                    totalUnrealizedPnL += pnl;
                    totalMarginHeld += (position.AverageEntryPrice * position.Quantity);
                }
            }

            return new PortfolioStateModel
            {
                SessionId = _sessionId,
                Cash = _currentCash,
                OpenPositionsJson = JsonConvert.SerializeObject(_openPositions.Values),
                AsOfDate = DateTime.UtcNow,
                UnrealizedPnL = totalUnrealizedPnL,
                TotalEquity = _currentCash + totalMarginHeld + totalUnrealizedPnL
            };
        }

        public PortfolioStateModel GetPortfolioState(Dictionary<string, PriceModel> currentPrices)
        {
            decimal totalUnrealizedPnL = 0;
            decimal totalMarginHeld = 0;

            lock (_positionLock)
            {
                foreach (var position in _openPositions.Values)
                {
                    // Extract the ClosePrice from the PriceModel, fallback to EntryPrice if missing
                    decimal currentPrice = currentPrices.TryGetValue(position.Symbol, out var bar) ? bar.ClosePrice : position.AverageEntryPrice;

                    decimal pnl = position.Direction == PositionDirection.Long
                        ? (currentPrice - position.AverageEntryPrice) * position.Quantity
                        : (position.AverageEntryPrice - currentPrice) * position.Quantity;

                    totalUnrealizedPnL += pnl;
                    totalMarginHeld += (position.AverageEntryPrice * position.Quantity);
                }
            }

            return new PortfolioStateModel
            {
                SessionId = _sessionId,
                Cash = _currentCash,
                OpenPositionsJson = JsonConvert.SerializeObject(_openPositions.Values),
                AsOfDate = DateTime.UtcNow,
                UnrealizedPnL = totalUnrealizedPnL,
                TotalEquity = _currentCash + totalMarginHeld + totalUnrealizedPnL
            };
        }

        public bool HasOpenPosition(string symbol)
        {
            return _openPositions.ContainsKey(symbol);
        }
    }
}