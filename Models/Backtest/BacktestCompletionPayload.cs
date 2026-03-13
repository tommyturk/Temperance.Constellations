namespace Temperance.Constellations.Models
{
    public class BacktestCompletionPayload
    {
        public Guid RunId { get; set; }
        public Guid? SessionId { get; set; }
        public decimal FinalEquity { get; set; }
        public string StrategyName { get; set; }
        public List<string> Symbols { get; set; }
        public string Interval { get; set; }
        public DateTime LastBacktestEndDate { get; set; }
        public decimal? TotalReturn { get; set; }
        public decimal? SharpeRatio { get; set; }
        public decimal? MaxDrawdown { get; set; }
        public int TotalTrades { get; set; }
    }
}
