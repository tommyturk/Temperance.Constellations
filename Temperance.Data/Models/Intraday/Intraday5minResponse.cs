using Newtonsoft.Json;

namespace TradingApp.Data.Models.Intraday
{
    public class Intraday5minResponse
    {
        [JsonProperty("Meta Data")]
        public MetaData MetaData { get; set; }

        [JsonProperty("Time Series (5min)")]
        public Dictionary<string, TimeSeriesEntry> TimeSeries { get; set; }
    }
}
