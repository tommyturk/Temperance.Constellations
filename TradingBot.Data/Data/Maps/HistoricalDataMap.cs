using CsvHelper.Configuration;
using TradingBot.Data.Models.HistoricalData;

namespace TradingBot.Data.Data.Maps
{
    public class HistoricalDataMap : ClassMap<HistoricalData>
    {
        public HistoricalDataMap()
        {
            Map(m => m.Symbol).Name("symbol");
            Map(m => m.Date).Name("date");
            Map(m => m.OpenPrice).Name("open");
            Map(m => m.HighPrice).Name("high");
            Map(m => m.LowPrice).Name("low");
            Map(m => m.ClosePrice).Name("close");
            Map(m => m.Volume).Name("volume");
        }
    }

}
