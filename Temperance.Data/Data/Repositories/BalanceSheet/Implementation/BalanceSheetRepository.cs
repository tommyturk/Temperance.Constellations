using Dapper;
using Microsoft.Data.SqlClient;
using Temperance.Data.Data.Repositories.BalanceSheet.Interface;
using Temperance.Data.Models.Securities.BalanceSheet;
namespace Temperance.Data.Data.Repositories.BalanceSheet.Implementation
{
    public class BalanceSheetRepository : IBalanceSheetRepository
    {
        private readonly string _connectionString;
        public BalanceSheetRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<BalanceSheetModel> GetSecurityBalanceSheet(int securityId)
        {
            using var connection = new SqlConnection(_connectionString);

            var queryQuarterly = @"
                SELECT TOP(4) 
                    [SecurityID]
                    ,[Symbol]
                    ,[FiscalDateEnding]
                    ,[ReportedCurrency]
                    ,[TotalAssets]
                    ,[TotalCurrentAssets]
                    ,[CashAndCashEquivalentsAtCarryingValue]
                    ,[CashAndShortTermInvestments]
                    ,[Inventory]
                    ,[CurrentNetReceivables]
                    ,[TotalNonCurrentAssets]
                    ,[PropertyPlantEquipment]
                    ,[AccumulatedDepreciationAmortizationPPE]
                    ,[IntangibleAssets]
                    ,[IntangibleAssetsExcludingGoodwill]
                    ,[Goodwill]
                    ,[Investments]
                    ,[LongTermInvestments]
                    ,[ShortTermInvestments]
                    ,[OtherCurrentAssets]
                    ,[OtherNonCurrentAssets]
                    ,[TotalLiabilities]
                    ,[TotalCurrentLiabilities]
                    ,[CurrentAccountsPayable]
                    ,[DeferredRevenue]
                    ,[CurrentDebt]
                    ,[ShortTermDebt]
                    ,[TotalNonCurrentLiabilities]
                    ,[CapitalLeaseObligations]
                    ,[LongTermDebt]
                    ,[CurrentLongTermDebt]
                    ,[LongTermDebtNoncurrent]
                    ,[ShortLongTermDebtTotal]
                    ,[OtherCurrentLiabilities]
                    ,[OtherNonCurrentLiabilities]
                    ,[TotalShareholderEquity]
                    ,[TreasuryStock]
                    ,[RetainedEarnings]
                    ,[CommonStock]
                    ,[CommonStockSharesOutstanding]
                    ,[CreatedAt]
                FROM [TradingBotDb].[Financials].[BalanceSheetQuarterly]
                WHERE SecurityID = @SecurityId
                ORDER BY FiscalDateEnding DESC";

            var parameters = new { SecurityId = securityId };
            var quarterlyData = (await connection.QueryAsync<BalanceSheetQuarterly>(queryQuarterly, parameters)).ToList();

            var queryAnnual = @"
                SELECT TOP(4) 
                    [SecurityID]
                    ,[Symbol]
                    ,[FiscalDateEnding]
                    ,[ReportedCurrency]
                    ,[TotalAssets]
                    ,[TotalCurrentAssets]
                    ,[CashAndCashEquivalentsAtCarryingValue]
                    ,[CashAndShortTermInvestments]
                    ,[Inventory]
                    ,[CurrentNetReceivables]
                    ,[TotalNonCurrentAssets]
                    ,[PropertyPlantEquipment]
                    ,[AccumulatedDepreciationAmortizationPPE]
                    ,[IntangibleAssets]
                    ,[IntangibleAssetsExcludingGoodwill]
                    ,[Goodwill]
                    ,[Investments]
                    ,[LongTermInvestments]
                    ,[ShortTermInvestments]
                    ,[OtherCurrentAssets]
                    ,[OtherNonCurrentAssets]
                    ,[TotalLiabilities]
                    ,[TotalCurrentLiabilities]
                    ,[CurrentAccountsPayable]
                    ,[DeferredRevenue]
                    ,[CurrentDebt]
                    ,[ShortTermDebt]
                    ,[TotalNonCurrentLiabilities]
                    ,[CapitalLeaseObligations]
                    ,[LongTermDebt]
                    ,[CurrentLongTermDebt]
                    ,[LongTermDebtNoncurrent]
                    ,[ShortLongTermDebtTotal]
                    ,[OtherCurrentLiabilities]
                    ,[OtherNonCurrentLiabilities]
                    ,[TotalShareholderEquity]
                    ,[TreasuryStock]
                    ,[RetainedEarnings]
                    ,[CommonStock]
                    ,[CommonStockSharesOutstanding]
                    ,[CreatedAt]
                FROM [TradingBotDb].[Financials].[BalanceSheetAnnual]
                WHERE SecurityID = @SecurityId
                ORDER BY FiscalDateEnding DESC";

            var annualData = (await connection.QueryAsync<BalanceSheetAnnual>(queryAnnual, parameters)).ToList();

            var balanceSheetData = new BalanceSheetModel
            {
                AnnualReports = annualData,
                QuarterlyReports = quarterlyData,
                Symbol = annualData.FirstOrDefault()?.Symbol ?? quarterlyData.FirstOrDefault()?.Symbol
            };

            return balanceSheetData;
        }


        public async Task<bool> InsertSecuritiesBalanceSheetData(int securityId, BalanceSheetModel balanceSheetData, string symbol)
        {
            var annualSuccess = false;
            var quarterlySuccess = false;

            if (balanceSheetData.AnnualReports != null)
                annualSuccess = await InsertAnnualBalanceSheetData(securityId, balanceSheetData.AnnualReports, symbol);

            if (balanceSheetData.QuarterlyReports != null)
                quarterlySuccess = await InsertQuarterlyBalanceSheetData(securityId, balanceSheetData.QuarterlyReports, symbol);

            return annualSuccess || quarterlySuccess;
        }

        private async Task<bool> CheckIfBalanceSheetExists<T>(int securityId, List<T> reports) where T : IBalanceSheetReport
        {
            if (reports == null || !reports.Any())
                return false;

            using var connection = new SqlConnection(_connectionString);

            var tableName = typeof(T) == typeof(BalanceSheetAnnual) ?
                "[TradingBotDb].[Financials].[BalanceSheetAnnual]" :
                "[TradingBotDb].[Financials].[BalanceSheetQuarterly]";

            var query = $@"
                SELECT COUNT(1) 
                FROM {tableName} 
                WHERE SecurityID = @SecurityID 
                AND FiscalDateEnding IN @FiscalDateEndings";

            var fiscalDates = reports.Select(r => r.FiscalDateEnding).ToList();

            var count = await connection.ExecuteScalarAsync<int>(query, new
            {
                SecurityID = securityId,
                FiscalDateEndings = fiscalDates
            });

            return count > 0;
        }


        private async Task<bool> InsertAnnualBalanceSheetData(int securityId, List<BalanceSheetAnnual> annualReports, string symbol)
        {
            var existsCheck = await CheckIfBalanceSheetExists(securityId, annualReports);
            if (existsCheck)
                return false;

            using var connection = new SqlConnection(_connectionString);
            var query = @"
                INSERT INTO [TradingBotDb].[Financials].[BalanceSheetAnnual] 
                (
                    SecurityID, Symbol, FiscalDateEnding, ReportedCurrency, TotalAssets, 
                    TotalCurrentAssets, CashAndCashEquivalentsAtCarryingValue, CashAndShortTermInvestments, 
                    Inventory, CurrentNetReceivables, TotalNonCurrentAssets, PropertyPlantEquipment, 
                    AccumulatedDepreciationAmortizationPPE, IntangibleAssets, IntangibleAssetsExcludingGoodwill, 
                    Goodwill, Investments, LongTermInvestments, ShortTermInvestments, OtherCurrentAssets, 
                    OtherNonCurrentAssets, TotalLiabilities, TotalCurrentLiabilities, CurrentAccountsPayable, 
                    DeferredRevenue, CurrentDebt, ShortTermDebt, TotalNonCurrentLiabilities, CapitalLeaseObligations, 
                    LongTermDebt, CurrentLongTermDebt, LongTermDebtNoncurrent, ShortLongTermDebtTotal, 
                    OtherCurrentLiabilities, OtherNonCurrentLiabilities, TotalShareholderEquity, TreasuryStock, 
                    RetainedEarnings, CommonStock, CommonStockSharesOutstanding, CreatedAt
                ) 
                VALUES 
                (
                    @SecurityID, @Symbol, @FiscalDateEnding, @ReportedCurrency, @TotalAssets, 
                    @TotalCurrentAssets, @CashAndCashEquivalentsAtCarryingValue, @CashAndShortTermInvestments, 
                    @Inventory, @CurrentNetReceivables, @TotalNonCurrentAssets, @PropertyPlantEquipment, 
                    @AccumulatedDepreciationAmortizationPPE, @IntangibleAssets, @IntangibleAssetsExcludingGoodwill, 
                    @Goodwill, @Investments, @LongTermInvestments, @ShortTermInvestments, @OtherCurrentAssets, 
                    @OtherNonCurrentAssets, @TotalLiabilities, @TotalCurrentLiabilities, @CurrentAccountsPayable, 
                    @DeferredRevenue, @CurrentDebt, @ShortTermDebt, @TotalNonCurrentLiabilities, @CapitalLeaseObligations, 
                    @LongTermDebt, @CurrentLongTermDebt, @LongTermDebtNoncurrent, @ShortLongTermDebtTotal, 
                    @OtherCurrentLiabilities, @OtherNonCurrentLiabilities, @TotalShareholderEquity, @TreasuryStock, 
                    @RetainedEarnings, @CommonStock, @CommonStockSharesOutstanding, @CreatedAt
                )";

            var parameters = annualReports.Select(report => new
            {
                SecurityID = securityId,
                Symbol = symbol,
                report.FiscalDateEnding,
                report.ReportedCurrency,
                report.TotalAssets,
                report.TotalCurrentAssets,
                report.CashAndCashEquivalentsAtCarryingValue,
                report.CashAndShortTermInvestments,
                report.Inventory,
                report.CurrentNetReceivables,
                report.TotalNonCurrentAssets,
                report.PropertyPlantEquipment,
                report.AccumulatedDepreciationAmortizationPPE,
                report.IntangibleAssets,
                report.IntangibleAssetsExcludingGoodwill,
                report.Goodwill,
                report.Investments,
                report.LongTermInvestments,
                report.ShortTermInvestments,
                report.OtherCurrentAssets,
                report.OtherNonCurrentAssets,
                report.TotalLiabilities,
                report.TotalCurrentLiabilities,
                report.CurrentAccountsPayable,
                report.DeferredRevenue,
                report.CurrentDebt,
                report.ShortTermDebt,
                report.TotalNonCurrentLiabilities,
                report.CapitalLeaseObligations,
                report.LongTermDebt,
                report.CurrentLongTermDebt,
                report.LongTermDebtNoncurrent,
                report.ShortLongTermDebtTotal,
                report.OtherCurrentLiabilities,
                report.OtherNonCurrentLiabilities,
                report.TotalShareholderEquity,
                report.TreasuryStock,
                report.RetainedEarnings,
                report.CommonStock,
                report.CommonStockSharesOutstanding,
                CreatedAt = DateTime.UtcNow
            });

            return await connection.ExecuteAsync(query, parameters) > 0;
        }

        private async Task<bool> InsertQuarterlyBalanceSheetData(int securityId, List<BalanceSheetQuarterly> quarterlyReports, string symbol)
        {
            var existsCheck = await CheckIfBalanceSheetExists(securityId, quarterlyReports);
            if (existsCheck)
                return false;

            using var connection = new SqlConnection(_connectionString);
            var query = @"
                INSERT INTO [TradingBotDb].[Financials].[BalanceSheetQuarterly] 
                (
                    SecurityID, Symbol, FiscalDateEnding, ReportedCurrency, TotalAssets, 
                    TotalCurrentAssets, CashAndCashEquivalentsAtCarryingValue, CashAndShortTermInvestments, 
                    Inventory, CurrentNetReceivables, TotalNonCurrentAssets, PropertyPlantEquipment, 
                    AccumulatedDepreciationAmortizationPPE, IntangibleAssets, IntangibleAssetsExcludingGoodwill, 
                    Goodwill, Investments, LongTermInvestments, ShortTermInvestments, OtherCurrentAssets, 
                    OtherNonCurrentAssets, TotalLiabilities, TotalCurrentLiabilities, CurrentAccountsPayable, 
                    DeferredRevenue, CurrentDebt, ShortTermDebt, TotalNonCurrentLiabilities, CapitalLeaseObligations, 
                    LongTermDebt, CurrentLongTermDebt, LongTermDebtNoncurrent, ShortLongTermDebtTotal, 
                    OtherCurrentLiabilities, OtherNonCurrentLiabilities, TotalShareholderEquity, TreasuryStock, 
                    RetainedEarnings, CommonStock, CommonStockSharesOutstanding, CreatedAt
                ) 
                VALUES 
                (
                    @SecurityID, @Symbol, @FiscalDateEnding, @ReportedCurrency, @TotalAssets, 
                    @TotalCurrentAssets, @CashAndCashEquivalentsAtCarryingValue, @CashAndShortTermInvestments, 
                    @Inventory, @CurrentNetReceivables, @TotalNonCurrentAssets, @PropertyPlantEquipment, 
                    @AccumulatedDepreciationAmortizationPPE, @IntangibleAssets, @IntangibleAssetsExcludingGoodwill, 
                    @Goodwill, @Investments, @LongTermInvestments, @ShortTermInvestments, @OtherCurrentAssets, 
                    @OtherNonCurrentAssets, @TotalLiabilities, @TotalCurrentLiabilities, @CurrentAccountsPayable, 
                    @DeferredRevenue, @CurrentDebt, @ShortTermDebt, @TotalNonCurrentLiabilities, @CapitalLeaseObligations, 
                    @LongTermDebt, @CurrentLongTermDebt, @LongTermDebtNoncurrent, @ShortLongTermDebtTotal, 
                    @OtherCurrentLiabilities, @OtherNonCurrentLiabilities, @TotalShareholderEquity, @TreasuryStock, 
                    @RetainedEarnings, @CommonStock, @CommonStockSharesOutstanding, @CreatedAt
                )";

            var parameters = quarterlyReports.Select(report => new
            {
                SecurityID = securityId,
                Symbol = symbol,
                report.FiscalDateEnding,
                report.ReportedCurrency,
                report.TotalAssets,
                report.TotalCurrentAssets,
                report.CashAndCashEquivalentsAtCarryingValue,
                report.CashAndShortTermInvestments,
                report.Inventory,
                report.CurrentNetReceivables,
                report.TotalNonCurrentAssets,
                report.PropertyPlantEquipment,
                report.AccumulatedDepreciationAmortizationPPE,
                report.IntangibleAssets,
                report.IntangibleAssetsExcludingGoodwill,
                report.Goodwill,
                report.Investments,
                report.LongTermInvestments,
                report.ShortTermInvestments,
                report.OtherCurrentAssets,
                report.OtherNonCurrentAssets,
                report.TotalLiabilities,
                report.TotalCurrentLiabilities,
                report.CurrentAccountsPayable,
                report.DeferredRevenue,
                report.CurrentDebt,
                report.ShortTermDebt,
                report.TotalNonCurrentLiabilities,
                report.CapitalLeaseObligations,
                report.LongTermDebt,
                report.CurrentLongTermDebt,
                report.LongTermDebtNoncurrent,
                report.ShortLongTermDebtTotal,
                report.OtherCurrentLiabilities,
                report.OtherNonCurrentLiabilities,
                report.TotalShareholderEquity,
                report.TreasuryStock,
                report.RetainedEarnings,
                report.CommonStock,
                report.CommonStockSharesOutstanding,
                CreatedAt = DateTime.UtcNow
            });

            return await connection.ExecuteAsync(query, parameters) > 0;
        }
    }
}
