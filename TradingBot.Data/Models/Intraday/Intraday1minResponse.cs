using Newtonsoft.Json;
using TradingApp.Data.Models.Intraday;

namespace TradingBot.Data.Models.Intraday
{
    public class Intraday1minResponse
    {
        [JsonProperty("Meta Data")]
        public MetaData MetaData { get; set; }

        [JsonProperty("Time Series (1min)")]
        public Dictionary<string, TimeSeriesEntry> TimeSeries { get; set; }
    }
}
