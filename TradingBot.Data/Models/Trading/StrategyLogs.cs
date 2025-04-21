namespace TradingBot.Data.Models.Trading
{
    public class StrategyLog
    {
        public int LogID { get; set; }
        public int TradeID { get; set; }
        public decimal MovingAverage { get; set; }
        public decimal StandardDeviation { get; set; }
        public decimal UpperThreshold { get; set; }
        public decimal LowerThreshold { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
