namespace Temperance.Data.Models.Trading
{
    public class Position
    {
        public string Symbol { get; set; } = string.Empty;
        public double AverageEntryPrice { get; set; }
        public DateTime InitialEntryDate { get; set; }
        public int Quantity { get; set; }
        public PositionDirection Direction { get; set; }
        public int PyramidEntries { get; set; } = 1;
        public int SecurityID { get; set; }
        public double TotalEntryCost { get; set; }

        // --- NEW PROPERTIES FOR ADVANCED EXITS ---
        public double StopLossPrice { get; set; }
        public int BarsHeld { get; set; } = 0;

        // --- DEPRECATED (can be removed if no longer used elsewhere) ---
        [Obsolete("Use AverageEntryPrice for consistency.")]
        public double EntryPrice { get; set; }
        [Obsolete("Use InitialEntryDate for consistency.")]
        public DateTime EntryDate { get; set; }
    }
}
