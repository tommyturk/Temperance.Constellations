namespace Temperance.Data.Models.Trading
{
    public class Position
    {
        public string Symbol { get; set; } = string.Empty;
        public double AverageEntryPrice { get; set; }

        //----Deprecated----
        public double EntryPrice { get; set; }

        public DateTime InitialEntryDate { get; set; }

        //----Deprecated----
        public DateTime EntryDate { get; set; }

        public int Quantity { get; set; }
        public PositionDirection Direction { get; set; }
        public int PyramidEntries { get; set; } = 1;

        public int SecurityID { get; set; }

        public double TotalEntryCost { get; set; }

        //public string Symbol { get; set; } = string.Empty;
        //public int Quantity { get; set; }
        //public decimal AveragePrice { get; set; }
        //public decimal? UnrealizedPL { get; set; }
        //public string Status { get; set; } = "Open";
    }
}
