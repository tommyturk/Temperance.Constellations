using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Data.Models.HistoricalData
{
    public class SymbolCoverageBacktestModel
    {
        public string Symbol { get; set; }

        public string Interval { get; set; }

        public int Years { get; set; }

        public List<HistoricalPriceModel> HistoricalData { get; set; }
    }
}
