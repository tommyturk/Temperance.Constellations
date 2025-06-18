namespace Temperance.Data.Models.Strategy
{
    public class PairStrategyParameters
    {
        public int SpreadLookbackPeriod { get; set; } = 60;
        public double EntryZScoreThreshold { get; set; } = 2.0;
        public double ExitZoreThreshold { get; set; } = 0.5;
        public string SymbolA { get; set; } = string.Empty;
        public string SymbolB { get; set; } = string.Empty;
        public double HedgeRation { get; set; } = 0.0;
    }
}
