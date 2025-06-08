using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Data.Models.Tools
{
    public record ToolCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
    }
}
