using Azure.Identity;
using Microsoft.Extensions.Logging;
using Temperance.Data.Models.Trading;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class TransactionCostService : ITransactionCostService
    {
        private readonly ILogger<TransactionCostService> _logger;
        private readonly double _defaultSpreadPercentage = 0.0005;

        public TransactionCostService(ILogger<TransactionCostService> logger)
        {
            _logger = logger;
        }

        public Task<double> CalculateEntryCost(double entryPrice, SignalDecision signal, string symbol, string interval, DateTime timestamp)
        {
            double spreadAmount = entryPrice * _defaultSpreadPercentage;
            double effectivePrice;
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
        public Task<double> CalculateExitCost(double exitPrice, PositionDirection positionDirection, string symbol, string interval, DateTime timestamp)
        {
            double spreadAmount = exitPrice * _defaultSpreadPercentage;

            double effectivePrice;
            if (positionDirection == PositionDirection.Long)
                effectivePrice = exitPrice - (spreadAmount / 2);
            else if (positionDirection == PositionDirection.Short) 
                effectivePrice = exitPrice + (spreadAmount / 2); 
            else
                effectivePrice = exitPrice;
            return Task.FromResult(effectivePrice);
        }
        public Task<double> GetSpreadCost(double price, int quantity, string symbol, string interval, DateTime timestamp)
        {
            double spreadAmount = price * _defaultSpreadPercentage;
            double totalSpreadCost = spreadAmount * quantity;
            return Task.FromResult(totalSpreadCost);
        }

        public Task<double> GetSpreadCost(double price, double quantity, string symbol, string interval, DateTime timestamp)
        {
            double spreadAmount = price * _defaultSpreadPercentage;
            double totalSpreadCost = spreadAmount * quantity;
            return Task.FromResult(totalSpreadCost);
        }
        public async Task<double> CalculateTotalTradeCost(double entryPrice, double exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp)
        {
            double entrySpreadCost = await GetSpreadCost(entryPrice, quantity, symbol, interval, entryTimestamp);
            double exitSpreadCost = await GetSpreadCost(exitPrice, quantity, symbol, interval, exitTimestamp);
            return entrySpreadCost + exitSpreadCost;
        }
        public double CalculateEntryCost(double entryPrice, SignalDecision signal)
        {
            double spreadAmount = entryPrice * _defaultSpreadPercentage;

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

        public double CalculateExitCost(double exitPrice, PositionDirection positionDirection)
        {
            double spreadAmount = exitPrice * _defaultSpreadPercentage;
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

        public double CalculateTotalCost(double entryPrice, double exitPrie, SignalDecision entrySignal, PositionDirection exitPositionDirectionl, int quantity)
        {
            double effectiveEntryPrice = CalculateEntryCost(entryPrice, entrySignal);
            double effectiveExitPrice = CalculateExitCost(exitPrie, exitPositionDirectionl);

            if(entrySignal == SignalDecision.Buy)
                return (entryPrice - effectiveEntryPrice) + (effectiveExitPrice - exitPrie) * quantity;
            else
                return (effectiveEntryPrice - entryPrice) + (exitPrie - effectiveExitPrice) * quantity;
        }
    }
}
