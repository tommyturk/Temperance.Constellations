namespace Temperance.Data.Models.Backtest
{
    public class WalkForwardSession
    {
        public Guid SessionId { get; set; }
        public string StrategyName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }  
        public string Status { get; set; } = string.Empty;
        public double InitialCapital { get; set; }
        public double CurrentCapital { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int OptimizationWindowYears { get; set; }

        public int TradingWindowYears { get; set; }
    }
}
