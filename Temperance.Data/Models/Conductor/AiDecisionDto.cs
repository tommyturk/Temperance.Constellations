using Newtonsoft.Json;

namespace Temperance.Data.Models.Conductor
{
    public class AiDecisionDto
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty; // "BUY", "SELL", "HOLD"

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("reasoning")]
        public string? Reasoning { get; set; }
    }
}
