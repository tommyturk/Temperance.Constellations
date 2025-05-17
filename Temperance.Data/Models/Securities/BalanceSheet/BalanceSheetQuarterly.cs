using Newtonsoft.Json;
using Temperance.Utilities.Extensions;

namespace Temperance.Data.Models.Securities.BalanceSheet
{
    public class BalanceSheetQuarterly : IBalanceSheetReport
    {
        public string Symbol { get; set; }
        public DateTime FiscalDateEnding { get; set; }
        public string ReportedCurrency { get; set; }

        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalAssets { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalCurrentAssets { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CashAndCashEquivalentsAtCarryingValue { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CashAndShortTermInvestments { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? Inventory { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CurrentNetReceivables { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalNonCurrentAssets { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? PropertyPlantEquipment { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? AccumulatedDepreciationAmortizationPPE { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? IntangibleAssets { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? IntangibleAssetsExcludingGoodwill { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? Goodwill { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? Investments { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? LongTermInvestments { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? ShortTermInvestments { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? OtherCurrentAssets { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? OtherNonCurrentAssets { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalLiabilities { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalCurrentLiabilities { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CurrentAccountsPayable { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? DeferredRevenue { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CurrentDebt { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? ShortTermDebt { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalNonCurrentLiabilities { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CapitalLeaseObligations { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? LongTermDebt { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CurrentLongTermDebt { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? LongTermDebtNoncurrent { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? ShortLongTermDebtTotal { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? OtherCurrentLiabilities { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? OtherNonCurrentLiabilities { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TotalShareholderEquity { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? TreasuryStock { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? RetainedEarnings { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CommonStock { get; set; }
        [JsonConverter(typeof(DecimalConverter))]
        public decimal? CommonStockSharesOutstanding { get; set; }
    }
}
