namespace Temperance.Data.Models.Backtest
{
    public class BacktestCompletionPayload
    {
        public Guid RunId { get; set; }
        public Guid? SessionId { get; set; }
        public double FinalEquity { get; set; }
        public string StrategyName { get; set; }
        public List<string> Symbols { get; set; }
        public string Interval { get; set; }
        public DateTime LastBacktestEndDate { get; set; }
        public double? TotalReturn { get; set; }
        public double? SharpeRatio { get; set; }
        public double? MaxDrawdown { get; set; }
        public int TotalTrades { get; set; }
    }
}
