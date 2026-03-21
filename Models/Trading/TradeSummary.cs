namespace Temperance.Constellations.Models.Trading
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
        public decimal EntryPrice { get; set; }
        public decimal? ExitPrice { get; set; }
        public int Quantity { get; set; }
        public string Direction { get; set; } = string.Empty;
        public decimal? ProfitLoss { get; set; }
        public DateTime CreatedDate { get; set; }

        public decimal? CommissionCost { get; set; }
        public decimal? SlippageCost { get; set; }
        public decimal? OtherTransactionCost { get; set; }
        public decimal? TotalTransactionCost { get; set; }
        public decimal? GrossProfitLoss { get; set; }
        public int? HoldingPeriodMinutes { get; set; }
        public decimal? MaxAdverseExcursion { get; set; }
        public decimal? MaxFavorableExcursion { get; set; }
        public string? EntryReason { get; set; }
        public string? ExitReason { get; set; }

        public decimal? ReturnPercentage { get; set; }
        public DateTime HoldingPeriod { get; set; }
        public decimal TransactionCost { get; set; }

    }
}
