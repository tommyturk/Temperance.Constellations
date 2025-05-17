namespace Temperance.Data.Models.Securities.BalanceSheet
{
    public interface IBalanceSheetReport
    {
        string Symbol { get; set; }
        DateTime FiscalDateEnding { get; set; }
        string ReportedCurrency { get; set; }
        decimal? TotalAssets { get; set; }
        decimal? TotalCurrentAssets { get; set; }
        decimal? CashAndCashEquivalentsAtCarryingValue { get; set; }
        decimal? CashAndShortTermInvestments { get; set; }
        decimal? Inventory { get; set; }
        decimal? CurrentNetReceivables { get; set; }
        decimal? TotalNonCurrentAssets { get; set; }
        decimal? PropertyPlantEquipment { get; set; }
        decimal? AccumulatedDepreciationAmortizationPPE { get; set; }
        decimal? IntangibleAssets { get; set; }
        decimal? IntangibleAssetsExcludingGoodwill { get; set; }
        decimal? Goodwill { get; set; }
        decimal? Investments { get; set; }
        decimal? LongTermInvestments { get; set; }
        decimal? ShortTermInvestments { get; set; }
        decimal? OtherCurrentAssets { get; set; }
        decimal? OtherNonCurrentAssets { get; set; }
        decimal? TotalLiabilities { get; set; }
        decimal? TotalCurrentLiabilities { get; set; }
        decimal? CurrentAccountsPayable { get; set; }
        decimal? DeferredRevenue { get; set; }
        decimal? CurrentDebt { get; set; }
        decimal? ShortTermDebt { get; set; }
        decimal? TotalNonCurrentLiabilities { get; set; }
        decimal? CapitalLeaseObligations { get; set; }
        decimal? LongTermDebt { get; set; }
        decimal? CurrentLongTermDebt { get; set; }
        decimal? LongTermDebtNoncurrent { get; set; }
        decimal? ShortLongTermDebtTotal { get; set; }
        decimal? OtherCurrentLiabilities { get; set; }
        decimal? OtherNonCurrentLiabilities { get; set; }
        decimal? TotalShareholderEquity { get; set; }
        decimal? TreasuryStock { get; set; }
        decimal? RetainedEarnings { get; set; }
        decimal? CommonStock { get; set; }
        decimal? CommonStockSharesOutstanding { get; set; }
    }

}
