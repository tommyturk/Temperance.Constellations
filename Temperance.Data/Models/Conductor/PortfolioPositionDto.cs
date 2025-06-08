using Newtonsoft.Json;

namespace Temperance.Data.Models.Conductor
{
    public class PortfolioPositionDto
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("averageEntryPrice")]
        public decimal AverageEntryPrice { get; set; }
    }
}
