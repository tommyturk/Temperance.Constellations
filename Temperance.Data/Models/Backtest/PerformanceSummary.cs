namespace Temperance.Data.Models.Backtest
{
    public class PerformanceSummary
    {
        public string Symbol { get; set; }
        public decimal? SharpeRatio { get; set; }
        public decimal? ProfitLoss { get; set; }
        public int? TotalTrades { get; set; }
        public decimal? WinRate { get; set; }
    }
}
