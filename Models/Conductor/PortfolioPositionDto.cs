using Newtonsoft.Json;

namespace Temperance.Constellations.Models.Conductor
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
