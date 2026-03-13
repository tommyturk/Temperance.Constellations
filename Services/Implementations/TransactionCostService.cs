using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class TransactionCostService : ITransactionCostService
    {
        private readonly ILogger<TransactionCostService> _logger;
        private readonly decimal _defaultSpreadPercentage = 0.0005m; // 0.05%
        private readonly decimal _defaultCommissionPercentage = 0.0001m; // 0.01%
        private readonly decimal _defaultSlippagePercentage = 0.0002m; // 0.02%
        private readonly decimal _defaultOtherCostPercentage = 0.00005m; // 0.005%

        public TransactionCostService(ILogger<TransactionCostService> logger)
        {
            _logger = logger;
        }

        public Task<decimal> CalculateEntryCost(decimal entryPrice, SignalDecision signal, string symbol, string interval, DateTime timestamp)
        {
            decimal spreadAmount = entryPrice * _defaultSpreadPercentage;
            decimal effectivePrice;
            if (signal == SignalDecision.Buy)
                effectivePrice = entryPrice + (spreadAmount / 2);
            else if (signal == SignalDecision.Sell)
                effectivePrice = entryPrice - (spreadAmount / 2);
            else
            {
                _logger.LogWarning("Invalid signal type for entry cost calculation: {Signal}", signal);
                effectivePrice = entryPrice;
            }
            return Task.FromResult(effectivePrice);
        }
        public Task<decimal> CalculateExitCost(decimal exitPrice, PositionDirection positionDirection, string symbol, string interval, DateTime timestamp)
        {
            decimal spreadAmount = exitPrice * _defaultSpreadPercentage;

            decimal effectivePrice;
            if (positionDirection == PositionDirection.Long)
                effectivePrice = exitPrice - (spreadAmount / 2);
            else if (positionDirection == PositionDirection.Short)
                effectivePrice = exitPrice + (spreadAmount / 2);
            else
                effectivePrice = exitPrice;
            return Task.FromResult(effectivePrice);
        }
        public Task<decimal> GetSpreadCost(decimal price, int quantity, string symbol, string interval, DateTime timestamp)
        {
            decimal spreadAmount = price * _defaultSpreadPercentage;
            decimal totalSpreadCost = spreadAmount * quantity;
            return Task.FromResult(totalSpreadCost);
        }

        public Task<decimal> GetSpreadCost(decimal price, decimal quantity, string symbol, string interval, DateTime timestamp)
        {
            decimal spreadAmount = price * _defaultSpreadPercentage;
            decimal totalSpreadCost = spreadAmount * quantity;
            return Task.FromResult(totalSpreadCost);
        }

        public decimal CalculateEntryCost(decimal entryPrice, SignalDecision signal)
        {
            decimal spreadAmount = entryPrice * _defaultSpreadPercentage;

            if (signal == SignalDecision.Buy)
                return entryPrice + (spreadAmount / 2);
            else if (signal == SignalDecision.Sell)
                return entryPrice - (spreadAmount / 2);
            else
            {
                _logger.LogWarning("Invalid signal type for entry cost calculation: {Signal}", signal);
                return entryPrice;
            }
        }

        public decimal CalculateExitCost(decimal exitPrice, PositionDirection positionDirection)
        {
            decimal spreadAmount = exitPrice * _defaultSpreadPercentage;
            if (positionDirection == PositionDirection.Long)
                return exitPrice - (spreadAmount / 2);
            else if (positionDirection == PositionDirection.Short)
                return exitPrice + (spreadAmount / 2);
            else
            {
                _logger.LogWarning("Invalid position direction for exit cost calculation: {PositionDirection}", positionDirection);
                return exitPrice;
            }
        }

        public decimal CalculateTotalCost(decimal entryPrice, decimal exitPrie, SignalDecision entrySignal, PositionDirection exitPositionDirectionl, int quantity)
        {
            decimal effectiveEntryPrice = CalculateEntryCost(entryPrice, entrySignal);
            decimal effectiveExitPrice = CalculateExitCost(exitPrie, exitPositionDirectionl);

            if (entrySignal == SignalDecision.Buy)
                return (entryPrice - effectiveEntryPrice) + (effectiveExitPrice - exitPrie) * quantity;
            else
                return (effectiveEntryPrice - entryPrice) + (exitPrie - effectiveExitPrice) * quantity;
        }

        public async Task<decimal> CalculateTotalTradeCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp)
        {
            decimal entrySpreadCost = await GetSpreadCost(entryPrice, quantity, symbol, interval, entryTimestamp);
            decimal exitSpreadCost = await GetSpreadCost(exitPrice, quantity, symbol, interval, exitTimestamp);
            decimal commission = await CalculateCommissionCost(entryPrice, quantity, symbol, interval, entryTimestamp) +
                                await CalculateCommissionCost(exitPrice, quantity, symbol, interval, exitTimestamp);
            decimal slippage = await CalculateSlippageCost(entryPrice, quantity, entrySignal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short, symbol, interval, entryTimestamp) +
                              await CalculateSlippageCost(exitPrice, quantity, exitPositionDirection, symbol, interval, exitTimestamp);
            decimal otherCosts = await CalculateOtherCost(entryPrice, quantity, symbol, interval, entryTimestamp) +
                                await CalculateOtherCost(exitPrice, quantity, symbol, interval, exitTimestamp);

            return entrySpreadCost + exitSpreadCost + commission + slippage + otherCosts;
        }

        public async Task<decimal> CalculateTotalTradeCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, decimal quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp)
        {
            decimal entrySpreadCost = await GetSpreadCost(entryPrice, quantity, symbol, interval, entryTimestamp);
            decimal exitSpreadCost = await GetSpreadCost(exitPrice, quantity, symbol, interval, exitTimestamp);
            decimal commission = await CalculateCommissionCost(entryPrice, quantity, symbol, interval, entryTimestamp) +
                                await CalculateCommissionCost(exitPrice, quantity, symbol, interval, exitTimestamp);
            decimal slippage = await CalculateSlippageCost(entryPrice, quantity, entrySignal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short, symbol, interval, entryTimestamp) +
                              await CalculateSlippageCost(exitPrice, quantity, exitPositionDirection, symbol, interval, exitTimestamp);
            decimal otherCosts = await CalculateOtherCost(entryPrice, quantity, symbol, interval, entryTimestamp) +
                                await CalculateOtherCost(exitPrice, quantity, symbol, interval, exitTimestamp);

            return entrySpreadCost + exitSpreadCost + commission + slippage + otherCosts;
        }

        public Task<decimal> CalculateCommissionCost(decimal price, decimal quantity, string symbol, string interval, DateTime timestamp)
        {
            return Task.FromResult(price * quantity * _defaultCommissionPercentage);
        }

        public Task<decimal> CalculateSlippageCost(decimal price, decimal quantity, PositionDirection direction, string symbol, string interval, DateTime timestamp)
        {
            return Task.FromResult(price * quantity * _defaultSlippagePercentage);
        }

        public Task<decimal> CalculateOtherCost(decimal price, decimal quantity, string symbol, string interval, DateTime timestamp)
        {
            return Task.FromResult(price * quantity * _defaultOtherCostPercentage);
        }
    }
}
