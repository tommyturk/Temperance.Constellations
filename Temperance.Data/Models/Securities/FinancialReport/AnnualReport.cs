namespace Temperance.Data.Models.Securities.FinancialReport
{
    public class AnnualReport
    {
        public string FiscalDateEnding { get; set; }
        public string ReportedCurrency { get; set; }
        public long OperatingCashflow { get; set; }
        public long PaymentsForOperatingActivities { get; set; }
        public string ProceedsFromOperatingActivities { get; set; }
        public long ChangeInOperatingLiabilities { get; set; }
        public long ChangeInOperatingAssets { get; set; }
        public long DepreciationDepletionAndAmortization { get; set; }
        public long CapitalExpenditures { get; set; }
        public long ChangeInReceivables { get; set; }
        public long ChangeInInventory { get; set; }
        public long ProfitLoss { get; set; }
        public long CashflowFromInvestment { get; set; }
        public long CashflowFromFinancing { get; set; }
        public long ProceedsFromRepaymentsOfShortTermDebt { get; set; }
        public long PaymentsForRepurchaseOfCommonStock { get; set; }
        public long PaymentsForRepurchaseOfEquity { get; set; }
        public string PaymentsForRepurchaseOfPreferredStock { get; set; }
        public long DividendPayout { get; set; }
        public long DividendPayoutCommonStock { get; set; }
        public string DividendPayoutPreferredStock { get; set; }
        public string ProceedsFromIssuanceOfCommonStock { get; set; }
        public long ProceedsFromIssuanceOfLongTermDebtAndCapitalSecuritiesNet { get; set; }
        public string ProceedsFromIssuanceOfPreferredStock { get; set; }
        public long ProceedsFromRepurchaseOfEquity { get; set; }
        public string ProceedsFromSaleOfTreasuryStock { get; set; }
        public string ChangeInCashAndCashEquivalents { get; set; }
        public string ChangeInExchangeRate { get; set; }
        public long NetIncome { get; set; }
    }
}
