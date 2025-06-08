using Newtonsoft.Json;

namespace Temperance.Data.Models.Conductor
{
    public class TradeRequestDto
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("action")] // "Buy" or "Sell"
        public string Action { get; set; }

        [JsonProperty("orderType")] // "market", "limit"
        public string OrderType { get; set; }

        [JsonProperty("timeInForce")] // "day", "gtc"
        public string TimeInForce { get; set; }

        [JsonProperty("limitPrice")] // Optional, for limit orders
        public decimal? LimitPrice { get; set; }
    }
}
