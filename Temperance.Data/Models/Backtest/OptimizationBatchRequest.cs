namespace Temperance.Data.Models.Backtest
{
    public class OptimizationBatchRequest
    {
        public Guid SessionId { get; set; }
        public string StrategyName { get; set; }
        public OptimizationMode Mode { get; set; }
        public DateTime InSampleStartDate { get; set; }
        public DateTime InSampleEndDate { get; set; }
        public List<string> Symbols { get; set; }
        public string Interval { get; set; } = "60min";
    }
}
