namespace Temperance.Data.Models.Backtest
{
    public class StrategyOptimizedParameters
    {
        public int Id { get; set; }
        public string StrategyName { get; set; }
        public string Symbol { get; set; }
        public string Interval { get; set; }
        public string OptimizedParametersJson { get; set; }
        public string Metrics { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public Guid JobId { get; set; }
        public Guid SessionId { get; set; }
        public string ResultKey { get; set; }
    }
}
