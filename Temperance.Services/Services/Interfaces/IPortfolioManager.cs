using Temperance.Data.Models.Trading;

namespace Temperance.Services.Services.Interfaces
{
    public interface IPortfolioManager
    {
        Task Initialize(decimal initialCapital);
        decimal GetAvailableCapital();
        decimal GetTotalEquity();
        decimal GetAllocatedCapital();
        IReadOnlyList<Position> GetOpenPositions();

        IReadOnlyList<TradeSummary> GetCompletedTradesHistory();

        Task<bool> CanOpenPosition(decimal allocationAmount);

        Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, decimal entryPrice, DateTime entryDate, decimal transactionCost);

        Task ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, decimal exitPrice, DateTime exitDate, decimal transactionCost, decimal profitLoss);
    }
}
