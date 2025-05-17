using Newtonsoft.Json;
using System.Text.Json;

namespace Temperance.Data.Models.HistoricalData
{
    public class HistoricalData
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("date")]
        public DateTime Date { get; set; }

        [JsonProperty("open")]
        public decimal OpenPrice { get; set; }

        [JsonProperty("high")]
        public decimal HighPrice { get; set; }

        [JsonProperty("low")]
        public decimal LowPrice { get; set; }

        [JsonProperty("close")]
        public decimal ClosePrice { get; set; }

        [JsonProperty("volume")]
        public long Volume { get; set; }

        public string TimeInterval { get; set; }
    }
}
