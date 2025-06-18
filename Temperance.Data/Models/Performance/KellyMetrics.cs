namespace Temperance.Data.Models.Performance
{
    public class KellyMetrics
    {
        public double KellyFraction { get; set; }
        public double KellyHalfFraction { get; set; }
        public double WinRate { get; set; }
        public double PayoffRatio { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double AverageWin { get; set; }
        public double AverageLoss { get; set; }

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
