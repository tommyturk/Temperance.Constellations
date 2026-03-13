namespace Temperance.Constellations.Models.Strategy
{
    public class PairStrategyParameters
    {
        public int SpreadLookbackPeriod { get; set; } = 60;
        public decimal EntryZScoreThreshold { get; set; } = 2.0m;
        public decimal ExitZoreThreshold { get; set; } = 0.5m;
        public string SymbolA { get; set; } = string.Empty;
        public string SymbolB { get; set; } = string.Empty;
        public decimal HedgeRation { get; set; } = 0.0m;
    }
}
