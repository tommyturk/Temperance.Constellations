using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Temperance.Data.Models.Conductor
{
    public class SimpleQuoteDto
    {
        [JsonPropertyName("ap")]
        public decimal AskPrice { get; set; }

        [JsonPropertyName("as")]
        public long AskSize { get; set; } // Typically in actual shares for US equities

        [JsonPropertyName("ax")]
        public string? AskExchange { get; set; }

        [JsonPropertyName("bp")]
        public decimal BidPrice { get; set; }

        [JsonPropertyName("bs")]
        public long BidSize { get; set; } // Typically in actual shares

        [JsonPropertyName("bx")]
        public string? BidExchange { get; set; }

        [JsonPropertyName("c")]
        public List<string>? Conditions { get; set; }

        [JsonPropertyName("t")]
        public DateTimeOffset TimestampUtc { get; set; } // DateTimeOffset is good for UTC timestamps

        [JsonPropertyName("z")]
        public string? Tape { get; set; }
    }
}
