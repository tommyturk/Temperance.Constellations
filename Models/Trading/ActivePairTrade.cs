namespace Temperance.Constellations.Models.Trading
{
    public record ActivePairTrade(
        string SymbolA,
        string SymbolB,
        decimal HedgeRatio,
        PositionDirection Direction,
        decimal QuantityA,
        decimal QuantityB,
        decimal EntryPriceA,
        decimal EntryPriceB,
        DateTime EntryDate,
        decimal TotalEntryTransactionCost
    );
}
