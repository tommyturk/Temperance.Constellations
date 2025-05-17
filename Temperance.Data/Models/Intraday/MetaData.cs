using Newtonsoft.Json;

namespace TradingApp.Data.Models.Intraday
{
    public class MetaData
    {
        [JsonProperty("1. Information")]
        public string Information { get; set; }

        [JsonProperty("2. Symbol")]
        public string Symbol { get; set; }

        [JsonProperty("3. LastRefreshed")]
        public string LastRefreshed { get; set; }

        [JsonProperty("4. Interval")]
        public string Interval { get; set; }

        [JsonProperty("5. OutputSize")]
        public string OutputSize { get; set; }

        [JsonProperty("6. TimeZone")]
        public string TimeZone { get; set; }
    }

}
