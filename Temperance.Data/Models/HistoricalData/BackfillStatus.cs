namespace Temperance.Data.Models.HistoricalData
{
    public class BackfillStatus
    {
        public string Symbol { get; set; }
        public string Interval { get; set; }
        public BackfillState Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double ProgressPercentage { get; set; }
        public string ErrorMessage { get; set; }
    }
}
