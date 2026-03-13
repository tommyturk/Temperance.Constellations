namespace Temperance.Constellations.Models
{
    public class PerformanceSummary
    {
        public string Symbol { get; set; }
        public decimal? SharpeRatio { get; set; }
        public decimal? ProfitLoss { get; set; }
        public int? TotalTrades { get; set; }
        public decimal? WinRate { get; set; }
        public decimal TotalTransactionCost { get; set; }
    }
}
