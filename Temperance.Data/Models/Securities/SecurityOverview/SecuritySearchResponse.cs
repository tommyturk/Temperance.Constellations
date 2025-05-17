using System.Text.Json.Serialization;

namespace Temperance.Data.Models.Securities.SecurityOverview
{
    public class SecuritySearchResponse
    {
        [JsonPropertyName("bestMatches")]
        public List<SecuritySearchResult> BestMatches { get; set; }
        
    }
}
