namespace Temperance.Data.Models.Backtest
{
    public class ShadowPerformance
    {
        public int ShadowPerformanceId { get; set; }
        public Guid RunId { get; set; }
        public string Symbol { get; set; }
        public decimal? SharpeRatio { get; set; }
        public decimal? ProfitLoss { get; set; }
        public int? TotalTrades { get; set; }
        public decimal? WinRate { get; set; }
    }
}
