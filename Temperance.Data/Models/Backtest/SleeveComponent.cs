namespace Temperance.Data.Models.Backtest
{
    public class SleeveComponent
    {
        public Guid SleeveComponentId { get; set; }
        public Guid RunId { get; set; }
        public Guid SessionId { get; set; }
        public string Symbol { get; set; }
        public decimal? SharpeRatio { get; set; }
        public decimal? ProfitLoss { get; set; }
        public int? TotalTrades { get; set; }
        public decimal? WinRate { get; set; }
    }
}
