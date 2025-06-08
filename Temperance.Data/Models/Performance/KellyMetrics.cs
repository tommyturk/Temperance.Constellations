namespace Temperance.Data.Models.Performance
{
    public class KellyMetrics
    {
        public decimal KellyFraction { get; set; }
        public decimal KellyHalfFraction { get; set; }
        public decimal WinRate { get; set; }
        public decimal PayoffRatio { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public decimal AverageWin { get; set; }
        public decimal AverageLoss { get; set; }

        public KellyMetrics()
        {
            KellyFraction = 0;
            KellyHalfFraction = 0;
            WinRate = 0;
            PayoffRatio = 0;
            TotalTrades = 0;
            WinningTrades = 0;
            LosingTrades = 0;
            AverageWin = 0;
            AverageLoss = 0;
        }
    }
}
