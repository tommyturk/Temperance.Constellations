﻿using Newtonsoft.Json;

namespace TradingApp.Data.Models.Intraday
{
    public class TimeSeriesEntry
    {
        [JsonProperty("1. open")]
        public decimal Open { get; set; }

        [JsonProperty("2. high")]
        public decimal High { get; set; }

        [JsonProperty("3. low")]
        public decimal Low { get; set; }

        [JsonProperty("4. close")]
        public decimal Close { get; set; }

        [JsonProperty("5. volume")]
        public long Volume { get; set; }
    }
}
