using Newtonsoft.Json;
using Temperance.Utilities.Extensions;

namespace Temperance.Data.Models.Securities.Earnings
{
    public class AnnualEarnings
    {
        public int Id { get; set; }

        public int SecurityId { get; set; }

        public string Symbol { get; set; }

        public DateTime FiscalDateEnding { get; set; }

        [JsonConverter(typeof(DecimalConverter))]
        public decimal ReportedEPS { get; set; }
    }
}
