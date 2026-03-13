using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Models
{
    public class SymbolCoverageBacktestModel
    {
        public string Symbol { get; set; }

        public string Interval { get; set; }

        public int Years { get; set; }

        public List<PriceModel> HistoricalData { get; set; }
    }
}
