using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Data.Models.Conductor
{
    public class AiTradingDecisionResponseDto
    {
        [JsonProperty("decisions")]
        public List<AiDecisionDto>? Decisions { get; set; }

        [JsonProperty("overallReasoningMemo")]
        public string? OverallReasoningMemo { get; set; }

        [JsonProperty("error")] // Optional: if Conductor or Athena encountered an issue
        public string? Error { get; set; }
    }
}
