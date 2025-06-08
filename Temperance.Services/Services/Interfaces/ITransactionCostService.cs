using Temperance.Data.Models.Trading;

namespace Temperance.Services.Services.Interfaces
{
    public interface ITransactionCostService
    {
        decimal CalculateEntryCost(decimal entryPrice, SignalDecision signal);
        decimal CalculateExitCost(decimal exitPrice, PositionDirection positionDirection);
        decimal CalculateTotalCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity);
        Task<decimal> CalculateEntryCost(decimal entryPrice, SignalDecision signal, string symbol, string interval, DateTime timestamp);
        Task<decimal> CalculateExitCost(decimal exitPrice, PositionDirection positionDirection, string symbol, string interval, DateTime timestamp);
        Task<decimal> GetSpreadCost(decimal price, int quantity, string symbol, string interval, DateTime timestamp);
        Task<decimal> CalculateTotalTradeCost(decimal entryPrice, decimal exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp);
    }
}
