namespace Temperance.Data.Models.Trading
{
    public class Position
    {
        public string Symbol { get; set; } = string.Empty;
        public string Interval { get; set; } = string.Empty;
        public double AverageEntryPrice { get; set; }
        public DateTime InitialEntryDate { get; set; }
        public int Quantity { get; set; }
        public PositionDirection Direction { get; set; }
        public int PyramidEntries { get; set; } = 1;
        public int SecurityID { get; set; }
        public double TotalEntryCost { get; set; }
        public double StopLossPrice { get; set; }
        public int BarsHeld { get; set; } = 0;
        public double EntryPrice { get; set; }
        public DateTime EntryDate { get; set; }
        public double AveragePrice { get; set; }
        public double CurrentMarketValue { get; set; }
        public double CostBasis => Quantity * AveragePrice;
        public double UnrealizedPnL => CurrentMarketValue - CostBasis;
    }
}
