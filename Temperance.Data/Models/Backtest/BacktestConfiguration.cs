namespace Temperance.Data.Models.Backtest
{
    public class BacktestConfiguration
    {
        public string StrategyName { get; set; } = string.Empty;
        public Dictionary<string, object> StrategyParameters { get; set; } = new();
        public List<string> Symbols { get; set; } = new();
        public List<string> Intervals { get; set; } = new(); 
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public double InitialCapital { get; set; } = 10000;
        public int MaxParallelism { get; set; } = Environment.ProcessorCount;
    }
}
