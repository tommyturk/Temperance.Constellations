namespace Temperance.Constellations.Models
{
    public class OptimizationJob
    {
        public Guid JobId { get; set; }
        public Guid SessionId { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime InSampleEndDate { get; set; }
        public string Symbol { get; set; }
        public string ResultKey { get; set; }
        public string OptimizedParametersJson { get; set; }
    }
}
