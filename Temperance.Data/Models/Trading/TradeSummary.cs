namespace Temperance.Data.Models.Trading
{
    public class TradeSummary
    {
        public Guid RunId { get; set; }
        public string Symbol { get; set; }
        public string Interval { get; set; }
        public string StrategyName { get; set; }
        public DateTime EntryDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public double EntryPrice { get; set; }
        public double? ExitPrice { get; set; }
        public int Quantity { get; set; }
        public string Direction { get; set; } 
        public double? ProfitLoss { get; set; } 
        public DateTime CreatedDate { get; set; } 

        public double? CommissionCost { get; set; }
        public double? SlippageCost { get; set; }
        public double? OtherTransactionCost { get; set; }
        public double? TotalTransactionCost { get; set; } 

        public double? GrossProfitLoss { get; set; } // Profit/Loss before any transaction costs
        public int? HoldingPeriodMinutes { get; set; }
        public double? MaxAdverseExcursion { get; set; } // Max unrealized loss during the trade
        public double? MaxFavorableExcursion { get; set; } // Max unrealized profit during the trade
        public string? EntryReason { get; set; } // Why the trade was entered (e.g., "RSI Buy Signal")
        public string? ExitReason { get; set; } // Why the trade was exited (e.g., "Stop Loss", "Take Profit", "Time Exit")
    }
}
