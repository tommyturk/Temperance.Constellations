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
    }
}
