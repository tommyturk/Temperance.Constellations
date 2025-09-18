namespace Temperance.Data.Models.Backtest
{
    public class BacktestConfiguration
    {
        public Guid RunId { get; set; }
        public Guid? SessionId { get; set; } 
        public int? OptimizationResultId { get; set; }
        public string StrategyName { get; set; } = string.Empty;
        public Dictionary<string, object> StrategyParameters { get; set; } = new();
        public Dictionary<string, Dictionary<string, object>> PortfolioParameters { get; set; }
        public List<string> Symbols { get; set; } = new();
        public List<string> Intervals { get; set; } = new(); 
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public double InitialCapital { get; set; } = 10000;
        public int MaxParallelism { get; set; } = Environment.ProcessorCount;

        public bool UseMocExit { get; set; } = false;
    }
}
