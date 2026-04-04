namespace Temperance.Constellations.Models.Trading
{
    public class Position
    {
        public string Symbol { get; set; } = string.Empty;
        public string Interval { get; set; } = string.Empty;
        public decimal AverageEntryPrice { get; set; }
        public DateTime InitialEntryDate { get; set; }
        public int Quantity { get; set; }
        public PositionDirection Direction { get; set; }
        public int PyramidEntries { get; set; } = 1;
        public int SecurityID { get; set; }
        public decimal TotalEntryCost { get; set; }
        public decimal StopLossPrice { get; set; }
        public int BarsHeld { get; set; } = 0;
        public decimal EntryPrice { get; set; }
        public DateTime EntryDate { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentMarketValue { get; set; }
        public decimal CostBasis => Quantity * AveragePrice;
        public decimal UnrealizedPnL => CurrentMarketValue - CostBasis;
        public decimal HighestPriceSinceEntry { get; set; }
        public decimal LowestPriceSinceEntry { get; set; }
    }
}
