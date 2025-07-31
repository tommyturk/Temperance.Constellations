using Temperance.Data.Models.Trading;

namespace Temperance.Services.Services.Interfaces
{
    public interface ITransactionCostService
    {
        double CalculateEntryCost(double entryPrice, SignalDecision signal);
        double CalculateExitCost(double exitPrice, PositionDirection positionDirection);
        double CalculateTotalCost(double entryPrice, double exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity);
        Task<double> CalculateEntryCost(double entryPrice, SignalDecision signal, string symbol, string interval, DateTime timestamp);
        Task<double> CalculateExitCost(double exitPrice, PositionDirection positionDirection, string symbol, string interval, DateTime timestamp);
        Task<double> GetSpreadCost(double price, int quantity, string symbol, string interval, DateTime timestamp);
        Task<double> GetSpreadCost(double price, double quantity, string symbol, string interval, DateTime timestamp);
        Task<double> CalculateTotalTradeCost(double entryPrice, double exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, int quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp);

        Task<double> CalculateTotalTradeCost(double entryPrice, double exitPrice, SignalDecision entrySignal, PositionDirection exitPositionDirection, double quantity, string symbol, string interval, DateTime entryTimestamp, DateTime exitTimestamp);
        Task<double> CalculateCommissionCost(double price, double quantity, string symbol, string interval, DateTime timestamp);
        Task<double> CalculateSlippageCost(double price, double quantity, PositionDirection direction, string symbol, string interval, DateTime timestamp);
        Task<double> CalculateOtherCost(double price, double quantity, string symbol, string interval, DateTime timestamp);
    }
}
