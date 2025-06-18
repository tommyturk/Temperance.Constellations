namespace Temperance.Data.Models.Strategy
{
    public class PairsBacktestConfiguration
    {
        public string StrategyName { get; set; }
        public PairStrategyParameters StrategyParameters { get; set; }
        public List<PairDefinition> PairsToTest { get; set; }
        public string Interval { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public double InitialCapital { get; set; } = 10000;
        public int MaxParallelism { get; set; } = Environment.ProcessorCount / 4;
    }
}
