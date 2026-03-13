using Newtonsoft.Json;

namespace Temperance.Constellations.Models.Tools
{
    public record ToolCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
    }
}
