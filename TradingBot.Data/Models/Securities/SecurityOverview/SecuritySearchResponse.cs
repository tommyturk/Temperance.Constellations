using System.Text.Json.Serialization;

namespace TradingBot.Data.Models.Securities.SecurityOverview
{
    public class SecuritySearchResponse
    {
        [JsonPropertyName("bestMatches")]
        public List<SecuritySearchResult> BestMatches { get; set; }
        
    }
}
