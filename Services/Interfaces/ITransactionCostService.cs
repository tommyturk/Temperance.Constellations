
using Temperance.Constellations.Models.Trading;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface ITransactionCostService
    {
        decimal CalculateEntryCost(decimal entryPrice, SignalDecision signal);
        decimal CalculateExitCost(decimal exitPrice, PositionDirection positionDirection);
        decimal CalculateTotalCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity);
        decimal CalculateEntryCost(decimal entryPrice, SignalDecision signal, string symbol, string interval, DateTime timestamp);
        decimal CalculateExitCost(decimal exitPrice, PositionDirection positionDirection, string symbol, string interval, DateTime timestamp);
        decimal GetSpreadCost(decimal price, int quantity, string symbol, string interval, DateTime timestamp);
        Task<decimal> GetSpreadCost(decimal price, decimal quantity, string symbol, string interval, DateTime timestamp);
        Task<decimal> CalculateTotalTradeCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp);

        Task<decimal> CalculateTotalTradeCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, decimal quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp);
        Task<decimal> CalculateCommissionCost(decimal price, decimal quantity, string symbol, string interval, DateTime timestamp);
        Task<decimal> CalculateSlippageCost(decimal price, decimal quantity, PositionDirection direction, string symbol, string interval, DateTime timestamp);
        Task<decimal> CalculateOtherCost(decimal price, decimal quantity, string symbol, string interval, DateTime timestamp);
        bool IsTradeEconomicallyViable(
                string symbol,
                decimal currentPrice,
                decimal atr,
                SignalDecision signal,
                string interval,
                DateTime timestamp);
    }
}
