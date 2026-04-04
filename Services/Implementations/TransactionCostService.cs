using Temperance.Constellations.Models.Policy;
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

        public decimal CalculateEntryCost(decimal entryPrice, SignalDecision signal, string symbol, string interval, DateTime timestamp)
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
            return effectivePrice;
        }
        public decimal CalculateExitCost(decimal exitPrice, PositionDirection positionDirection, string symbol, string interval, DateTime timestamp)
        {
            decimal spreadAmount = exitPrice * _defaultSpreadPercentage;

            decimal effectivePrice;
            if (positionDirection == PositionDirection.Long)
                effectivePrice = exitPrice - (spreadAmount / 2);
            else if (positionDirection == PositionDirection.Short)
                effectivePrice = exitPrice + (spreadAmount / 2);
            else
                effectivePrice = exitPrice;
            return effectivePrice;
        }
        public decimal GetSpreadCost(decimal price, int quantity, string symbol, string interval, DateTime timestamp)
        {
            decimal spreadAmount = price * _defaultSpreadPercentage;
            decimal totalSpreadCost = spreadAmount * quantity;
            return totalSpreadCost;
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
            decimal entrySpreadCost = GetSpreadCost(entryPrice, quantity, symbol, interval, entryTimestamp);
            decimal exitSpreadCost = GetSpreadCost(exitPrice, quantity, symbol, interval, exitTimestamp);
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

        public bool IsTradeEconomicallyViable(
            string symbol,
            decimal currentPrice,
            decimal atr,
            SignalDecision signal,
            string interval,
            DateTime timestamp)
        {
            // 1. Calculate Entry Cost (Slippage/Fees for buying/shorting)
            decimal entryPriceAdjusted = CalculateEntryCost(currentPrice, signal, symbol, interval, timestamp);
            decimal entryCost = Math.Abs(entryPriceAdjusted - currentPrice);

            // 2. Calculate Exit Cost (Assuming we exit via a reversal signal later)
            var exitSignal = (signal == SignalDecision.Buy) ? SignalDecision.Sell : SignalDecision.Buy;
            decimal exitPriceAdjusted = CalculateEntryCost(currentPrice, exitSignal, symbol, interval, timestamp);
            decimal exitCost = Math.Abs(exitPriceAdjusted - currentPrice);

            // 3. Add Spread/Commission (Fixed costs)
            // Assuming 100 shares as a "unit" for cost estimation
            decimal explicitFees = GetSpreadCost(currentPrice, 100, symbol, interval, timestamp) / 100;

            decimal totalRoundTripCost = entryCost + exitCost + explicitFees;

            // 4. THE CARVER FILTER: Cost vs. Volatility
            // If our costs are $0.50 but the stock only moves $1.00 a day (ATR), 
            // we are losing 50% of the "Alpha" to the broker.
            if (atr <= 0) return false;

            decimal costToVolRatio = totalRoundTripCost / atr;

            // 5. Threshold: 15% is the Institutional "Pain" limit.
            // If costs > 15% of the daily ATR, the Sharpe Ratio will likely be negative.
            if (costToVolRatio > TradingEnginePolicy.MAX_TRANSACTION_COST_TO_ATR_RATIO_THRESHOLD)
            {
                return false;
            }

            return true;
        }
    }
}
