namespace Temperance.Data.Models.Backtest
{
    public class OptimizationResultDto
    {
        public string Symbol { get; set; }
        public OptimizationMetrics Metrics {get;set;}
    }
}
