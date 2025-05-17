using Newtonsoft.Json;
using Temperance.Utilities.Extensions;
namespace Temperance.Data.Models.Securities.Earnings
{
    public class QuarterlyEarnings
    {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public DateTime FiscalDateEnding { get; set; }
        public DateTime ReportedDate { get; set; }

        [JsonConverter(typeof(DecimalConverter))]
        public decimal? ReportedEPS { get; set; }

        [JsonConverter(typeof(DecimalConverter))]
        public decimal? EstimatedEPS { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? Surprise { get; set; }

        [JsonProperty("surprisePercentage")]
        [JsonConverter(typeof(NullableDecimalConverter))]
        public decimal? SurprisePercentage { get; set; }

        public string ReportTime { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
