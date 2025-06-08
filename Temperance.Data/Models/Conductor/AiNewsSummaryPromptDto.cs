using Newtonsoft.Json;

namespace Temperance.Data.Models.Conductor
{
    public class AiNewsSummaryPromptDto
    {
        [JsonProperty("context")]
        public string Context { get; set; } = string.Empty;

        [JsonProperty("desiredSummaryLength")]
        public string? DesiredSummaryLength { get; set; } // e.g., "2-3 sentences"

        [JsonProperty("systemPrompt")] // Optional system prompt for the summarization task
        public string? SystemPrompt { get; set; }
    }
}
