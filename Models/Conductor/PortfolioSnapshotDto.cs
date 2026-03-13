using Newtonsoft.Json;

namespace Temperance.Constellations.Models.Conductor
{
    public class PortfolioSnapshotDto
    {
        [JsonProperty("cash")]
        public decimal Cash { get; set; }
        
        [JsonProperty("totalValue")]
        public decimal TotalValue { get; set; }
        
        [JsonProperty("positions")]
        public List<PortfolioPositionDto>? Positions { get; set; }
    }
}
