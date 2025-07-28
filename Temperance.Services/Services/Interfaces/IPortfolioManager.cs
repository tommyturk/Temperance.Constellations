using Temperance.Data.Models.Trading;

namespace Temperance.Services.Services.Interfaces
{
    public interface IPortfolioManager
    {
        Task Initialize(double initialCapital);
        double GetAvailableCapital();
        double GetTotalEquity();
        double GetAllocatedCapital();
        IReadOnlyList<Position> GetOpenPositions();

        IReadOnlyList<TradeSummary> GetCompletedTradesHistory();

        Task<bool> CanOpenPosition(double allocationAmount);

        Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate, double transactionCost);
        Task OpenPosition(string symbol, string interval, PositionDirection direction, int quantity, double entryPrice, DateTime entryDate,
                          double spreadCost, double commissionCost, double slippageCost, double otherCost, string entryReason);

        Task OpenPairPosition(string strategyName, string pairIdentifier, string interval, ActivePairTrade trade);

        Task<TradeSummary?> ClosePosition(string strategyName, string symbol, string interval, PositionDirection direction, int quantity, double exitPrice, DateTime exitDate,
                                         double entrySpreadCost, double entryCommissionCost, double entrySlippageCost, double entryOtherCost, 
                                         double exitSpreadCost, double exitCommissionCost, double exitSlippageCost, double exitOtherCost,
                                         double grossProfitLoss, int holdingPeriodMinutes, double maxAdverseExcursion, double maxFavorableExcursion, string exitReason);

        Task<TradeSummary?> ClosePairPosition(
            ActivePairTrade activeTrade,
            double exitPriceA,
            double exitPriceB,
            DateTime exitTimestamp,
            double exitSpreadCostA, double exitCommissionCostA, double exitSlippageCostA, double exitOtherCostA,
            double exitSpreadCostB, double exitCommissionCostB, double exitSlippageCostB, double exitOtherCostB,
            double grossProfitLoss, int holdingPeriodMinutes, double maxAdverseExcursion, double maxFavorableExcursion, string exitReason);
    }
}
