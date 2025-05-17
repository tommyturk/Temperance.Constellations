using Newtonsoft.Json;

namespace Temperance.Data.Models.Securities.Earnings
{
    public class Earnings
    {
        public string Symbol { get; set; }

        [JsonProperty("annualEarnings")]
        public List<AnnualEarnings> Annual { get; set; }

        [JsonProperty("quarterlyEarnings")]
        public List<QuarterlyEarnings> Quarterly { get; set; }
    }
}
