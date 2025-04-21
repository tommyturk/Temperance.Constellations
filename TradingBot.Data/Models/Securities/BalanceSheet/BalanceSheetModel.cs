namespace TradingBot.Data.Models.Securities.BalanceSheet
{
    public class BalanceSheetModel
    {
        public string Symbol { get; set; }
        public List<BalanceSheetAnnual> AnnualReports { get; set; }
        public List<BalanceSheetQuarterly> QuarterlyReports { get; set; }
    }
}
