namespace Temperance.Data.Models.Backtest
{
    public class BatchCompletionCheckRequest
    {
        public Guid SessionId { get; set; }
        public string StrategyName { get; set; }
        public OptimizationMode Mode { get; set; }
        public DateTime InSampleEndDate { get; set; }
        public int TotalSymbolsInBatch { get; set; }
        public DateTime? OutOfSampleStartDate { get; set; }
    }
}
