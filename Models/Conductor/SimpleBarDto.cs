using Newtonsoft.Json;

namespace Temperance.Constellations.Models.Conductor
{
    public class SimpleBarDto
    {
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("closePrice")]
        public decimal ClosePrice { get; set; }
    }
}
