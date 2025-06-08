using Newtonsoft.Json;

namespace Temperance.Data.Models.Tools
{
    public class JsonRpcReqest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpcVersion { get; } = "2.0";

        [JsonProperty("method")]
        public string Method { get; set; } = "tools/call";

        [JsonProperty("params")]
        public ToolCallParams Params { get; set; } = new ToolCallParams();

        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
}
