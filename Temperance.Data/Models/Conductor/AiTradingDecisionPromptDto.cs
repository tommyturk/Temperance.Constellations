using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Data.Models.Conductor
{
    public class AiTradingDecisionPromptDto
    {
        [JsonProperty("userPrompt")]
        public string UserPrompt { get; set; } = string.Empty; // The fully constructed user prompt for Claude

        [JsonProperty("systemPrompt")]
        public string? SystemPrompt { get; set; } // The system prompt for Claude
    }
}
