namespace Temperance.Data.Models.Trading
{
    public class TradeSummary
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RunId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Interval { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public double EntryPrice { get; set; }
        public double? ExitPrice { get; set; }
        public double? ProfitLoss { get; set; }
        public string Direction { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime HoldingPeriod { get; set; }
        public double TransactionCost { get; set; }

        public double? CommissionCost { get; set; }
        public double? SlippageCost { get; set; }
        public double? OtherTransactionCost { get; set; } 
        public double? TotalTransactionCost { get; set; }
        public double? GrossProfitLoss { get; set; }
        public int? HoldingPeriodMinutes { get; set; }
        public double? MaxAdverseExcursion { get; set; }
        public double? MaxFavorableExcursion { get; set; }
        public string? EntryReason { get; set; }
        public string? ExitReason { get; set; }
    }
}
