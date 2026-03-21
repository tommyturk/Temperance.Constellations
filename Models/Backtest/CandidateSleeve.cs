namespace Temperance.Constellations.Models.Backtest
{
    public class CandidateSleeve
    {
        public string Symbol { get; set; }
        public int OptimizationResultId { get; set; }
        public decimal ExpectedSharpe { get; set; }
        public decimal ShadowSharpe { get; set; }
        public decimal ShadowWinRate { get; set; }
        public decimal ShadowMaxDrawdown { get; set; }
        public decimal CompositeScore { get; set; }
        public string OptimizedParametersJson { get; set; }
        public bool IsPromoted { get; set; }
    }
}
