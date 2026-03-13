using Newtonsoft.Json;

namespace Temperance.Constellations.Models.Conductor
{
    public class TradeResponseDto
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("orderId")]
        public string? OrderId { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }
    }
}
