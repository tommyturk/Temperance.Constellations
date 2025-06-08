using Azure.Identity;
using Microsoft.Extensions.Logging;
using Temperance.Data.Models.Trading;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class TransactionCostService : ITransactionCostService
    {
        private readonly ILogger<TransactionCostService> _logger;
        private readonly decimal _defaultSpreadPercentage = 0.0005m;

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
        public async Task<decimal> CalculateTotalTradeCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp)
        {
            decimal entrySpreadCost = await GetSpreadCost(entryPrice, quantity, symbol, interval, entryTimestamp);
            decimal exitSpreadCost = await GetSpreadCost(exitPrice, quantity, symbol, interval, exitTimestamp);
            return entrySpreadCost + exitSpreadCost;
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

            if(entrySignal == SignalDecision.Buy)
                return (entryPrice - effectiveEntryPrice) + (effectiveExitPrice - exitPrie) * quantity;
            else
                return (effectiveEntryPrice - entryPrice) + (exitPrie - effectiveExitPrice) * quantity;
        }
    }
}
