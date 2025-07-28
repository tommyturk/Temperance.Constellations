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
        private readonly object _lock = new object();
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
            if (!canAfford) _logger.LogDebug("Insufficient funds to open position. Required: {AllocationAmount}, Available: {AvailableCash}", allocationAmount, _currentCash);

            return Task.FromResult(canAfford);
        }

        public async Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate, double transactionCost)
        {
            lock (_lock)
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
        }

        public async Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate,
                                       double spreadCost, double commissionCost, double slippageCost, double otherCost, string entryReason)
        {
            lock (_lock)
            {
                double totalTransactionCost = spreadCost + commissionCost + slippageCost + otherCost;
                double totalCashOutlay = (quantity * entryPrice) + totalTransactionCost;

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
                    EntryDate = entryDate,
                    EntryReason = entryReason 
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
        }

        public async Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade)
        {
            lock (_lock)
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

        public Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, double exitPrice, DateTime exitDate,
                                                 double entrySpreadCost, double entryCommissionCost, double entrySlippageCost, double entryOtherCost,
                                                 double exitSpreadCost, double exitCommissionCost, double exitSlippageCost, double exitOtherCost,
                                                 double grossProfitLoss, int holdingPeriodMinutes, double maxAdverseExcursion, double maxFavorableExcursion, string exitReason)
        {
            lock (_lock)
            {
                if (_openPositions.TryRemove(symbol, out var closedPosition))
                {
                    _allocatedCapital -= (closedPosition.Quantity * closedPosition.EntryPrice);

                    double totalTransactionCost = entrySpreadCost + entryCommissionCost + entrySlippageCost + entryOtherCost +
                                                  exitSpreadCost + exitCommissionCost + exitSlippageCost + exitOtherCost;

                    double netProfitLoss = grossProfitLoss - totalTransactionCost;

                    _currentCash += netProfitLoss;

                    var finalTradeSummary = new TradeSummary
                    {
                        StrategyName = strategyName,
                        EntryDate = closedPosition.EntryDate,
                        EntryPrice = closedPosition.EntryPrice,
                        ExitDate = exitDate,
                        ExitPrice = exitPrice,
                        Direction = direction == PositionDirection.Long ? "Long" : "Short",
                        Quantity = quantity,
                        ProfitLoss = netProfitLoss,
                        CreatedDate = DateTime.UtcNow,

                        CommissionCost = entryCommissionCost + exitCommissionCost,
                        SlippageCost = entrySlippageCost + exitSlippageCost,
                        OtherTransactionCost = entryOtherCost + exitOtherCost + entrySpreadCost + exitSpreadCost,
                        TotalTransactionCost = totalTransactionCost,

                        GrossProfitLoss = grossProfitLoss,
                        HoldingPeriodMinutes = holdingPeriodMinutes,
                        MaxAdverseExcursion = maxAdverseExcursion,
                        MaxFavorableExcursion = maxFavorableExcursion,
                        EntryReason = closedPosition.EntryReason,
                        ExitReason = exitReason,

                        Symbol = symbol,
                        Interval = interval
                    };

                    _completedTradesHistory.Add(finalTradeSummary);

                    _logger.LogInformation("Closed {Direction} position for {Quantity} shares of {Symbol}. Net PnL: {NetPnL:C}. Gross PnL: {GrossPnL:C}. Total Tx Cost: {TxCost:C}. Current Cash: {Cash:C}, Allocated: {Allocated:C}",
                        direction, quantity, symbol, netProfitLoss, grossProfitLoss, totalTransactionCost, _currentCash, _allocatedCapital);
                    return Task.FromResult<TradeSummary?>(finalTradeSummary);
                }
                else
                {
                    _logger.LogWarning("Attempted to close position for {Symbol} but no open position found. This might indicate a logic error or out-of-sync state.", symbol);
                    return Task.FromResult<TradeSummary?>(null);
                }
            }
        }

        public Task<TradeSummary?> ClosePairPosition(
            ActivePairTrade activeTrade,
            double exitPriceA,
            double exitPriceB,
            DateTime exitTimestamp,
            double exitSpreadCostA, double exitCommissionCostA, double exitSlippageCostA, double exitOtherCostA,
            double exitSpreadCostB, double exitCommissionCostB, double exitSlippageCostB, double exitOtherCostB,
            double grossProfitLoss, int holdingPeriodMinutes, double maxAdverseExcursion, double maxFavorableExcursion, string exitReason)
        {
            var pairIdentifier = $"{activeTrade.SymbolA}/{activeTrade.SymbolB}";

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

            // Remove the positions from the open list.
            _openPositions.Remove(positionA.Key, out Position valueA);
            _openPositions.Remove(positionB.Key, out Position valueB);

            // Calculate total transaction cost for the entire pair trade (entry + exit)
            double totalEntryTransactionCost = activeTrade.TotalEntryTransactionCost; // Assuming this is already the sum of individual entry costs
            double totalExitTransactionCost = exitSpreadCostA + exitCommissionCostA + exitSlippageCostA + exitOtherCostA +
                                              exitSpreadCostB + exitCommissionCostB + exitSlippageCostB + exitOtherCostB;
            double totalTradeTransactionCost = totalEntryTransactionCost + totalExitTransactionCost;

            // Net profit/loss is gross profit/loss minus total transaction costs
            double netProfitLoss = grossProfitLoss - totalTradeTransactionCost;

            // Update cash balance with net PnL
            _currentCash += netProfitLoss;

            // Update allocated capital (assuming it was allocated based on entry prices)
            _allocatedCapital -= ((positionA.Value.Quantity * positionA.Value.EntryPrice) + (positionB.Value.Quantity * positionB.Value.EntryPrice));


            // Create a single TradeSummary object to represent the aggregate result of the pair trade.
            var summary = new TradeSummary
            {
                Symbol = pairIdentifier, // Use a pair identifier
                Direction = activeTrade.Direction.ToString(), // "Long" or "Short" the spread
                EntryDate = activeTrade.EntryDate,
                ExitDate = exitTimestamp,
                EntryPrice = activeTrade.EntryPriceA, // Could be average or just A
                ExitPrice = exitPriceA, // Could be average or just A
                Quantity = (int)(activeTrade.QuantityA + activeTrade.QuantityB), // Total quantity of both legs
                ProfitLoss = netProfitLoss,
                CreatedDate = DateTime.UtcNow,

                // Populate new transaction cost fields - this might need more detail if ActivePairTrade
                // doesn't break down entry costs. For now, combining.
                CommissionCost = (activeTrade.EntryCommissionCost ?? 0) + exitCommissionCostA + exitCommissionCostB,
                SlippageCost = (activeTrade.EntrySlippageCost ?? 0) + exitSlippageCostA + exitSlippageCostB,
                OtherTransactionCost = (activeTrade.EntryOtherCost ?? 0) + exitOtherCostA + exitOtherCostB + activeTrade.EntrySpreadCost + exitSpreadCostA + exitSpreadCostB, // Sum of all "other" costs including spread
                TotalTransactionCost = totalTradeTransactionCost,

                // Populate new performance metrics
                GrossProfitLoss = grossProfitLoss,
                HoldingPeriodMinutes = holdingPeriodMinutes,
                MaxAdverseExcursion = maxAdverseExcursion,
                MaxFavorableExcursion = maxFavorableExcursion,
                EntryReason = activeTrade.EntryReason,
                ExitReason = exitReason,

                Interval = activeTrade.Interval // Assuming ActivePairTrade has Interval
            };

            // Add to completed trades and return.
            _completedTradesHistory.Add(summary);

            _logger.LogInformation("Closed pair position for {Pair}. Net PnL: {NetPnL:C}. Gross PnL: {GrossPnL:C}. Total Tx Cost: {TxCost:C}. Current Cash: {Cash:C}",
                pairIdentifier, netProfitLoss, grossProfitLoss, totalTradeTransactionCost, _currentCash);

            return Task.FromResult<TradeSummary?>(summary);
        }
    }
}
