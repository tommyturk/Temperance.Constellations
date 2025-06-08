namespace Temperance.Data.Models.Trading
{
    public class TradeSummary
    {
        public Guid RunId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Interval { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public DateTime EntryDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal? ExitPrice { get; set; }
        public decimal? ProfitLoss { get; set; }
        public string Direction { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime HoldingPeriod { get; set; }
        public decimal TransactionCost { get; set; }
    }
}
