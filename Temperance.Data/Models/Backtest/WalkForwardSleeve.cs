namespace Temperance.Data.Models.Backtest
{
    public class WalkForwardSleeve
    {
        public int SleeveId { get; set; }
        public Guid SessionId { get; set; }
        public DateTime TradingPeriodStartDate { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Interval { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty; 
        public int OptimizationResultId { get; set; }
        public double? InSampleSharpeRatio { get; set; }
        public double? InSampleMaxDrawdown { get; set; }
        public string OptimizedParametersJson { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
