namespace Temperance.Data.Models.Trading
{
    public class ActivePairTrade
    {
        public string SymbolA { get; set; }
        public string SymbolB { get; set; }
        public long HedgeRatio { get; set; }
        public PositionDirection Direction { get; set; }
        public long QuantityA { get; set; }
        public long QuantityB { get; set; }
        public double EntryPriceA { get; set; }
        public double EntryPriceB { get; set; }
        public DateTime EntryDate { get; set; }
        public double TotalEntryTransactionCost { get; set; } // Total of all entry costs

        // New properties for individual entry cost components
        public double? EntrySpreadCost { get; set; }
        public double? EntryCommissionCost { get; set; }
        public double? EntrySlippageCost { get; set; }
        public double? EntryOtherCost { get; set; }
        public string? EntryReason { get; set; }
        public string? Interval { get; set; } // Added interval
        public string? StrategyName { get; set; } // Added strategy name
    }
}
