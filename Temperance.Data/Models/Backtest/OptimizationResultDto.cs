namespace Temperance.Data.Models.Backtest
{
    public class OptimizationResultDto
    {
        public string Symbol { get; set; }
        public decimal InSampleSharpe { get; set; } // This is the performance metric
    }
}
