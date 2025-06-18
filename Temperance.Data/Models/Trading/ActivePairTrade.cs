namespace Temperance.Data.Models.Trading
{
    public record ActivePairTrade(
        string SymbolA,
        string SymbolB,
        double HedgeRatio,
        PositionDirection Direction,
        long QuantityA,
        long QuantityB,
        double EntryPriceA,
        double EntryPriceB,
        DateTime EntryDate,
        double TotalEntryTransactionCost
    );
}
