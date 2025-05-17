namespace Temperance.Data.Models.Trading
{
    public class Position
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public DateTime EntryDate { get; set; }
        public int Quantity { get; set; }
        public PositionDirection Direction { get; set; }
        //public int PositionID { get; set; }
        public int SecurityID { get; set; }
        //public string Symbol { get; set; } = string.Empty;
        //public int Quantity { get; set; }
        //public decimal AveragePrice { get; set; }
        //public decimal? UnrealizedPL { get; set; }
        //public string Status { get; set; } = "Open";
    }
}
