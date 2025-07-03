using Dapper;
using Microsoft.Data.SqlClient;
using Temperance.Data.Data.Repositories.Securities.Interfaces;
using Temperance.Data.Models.HistoricalData;

namespace Temperance.Data.Repositories.Securities.Implementations
{
    public class SecuritiesOverviewRepository : ISecuritiesOverviewRepository
    {
        private readonly string _connectionString;
        private static readonly object _lock = new object();
        public SecuritiesOverviewRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<string>> GetSecurities()
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT Symbol FROM [TradingBotDb].[Financials].[Securities]";
            return (await connection.QueryAsync<string>(query)).ToList();
        }

        public async Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbols = null)
        {
            await using var connection = new SqlConnection(_connectionString);

            var queryBuilder = new System.Text.StringBuilder();
            queryBuilder.AppendLine("SELECT");
            queryBuilder.AppendLine("    sdc.[Symbol], -- Use sdc.Symbol as it's the source for coverage");
            queryBuilder.AppendLine("    sdc.[Interval],");
            queryBuilder.AppendLine("    MAX(sdc.Year) - MIN(sdc.Year) AS Years");
            queryBuilder.AppendLine("FROM [Historical].[BackFill].[SecurityDataCoverage] AS sdc");

            queryBuilder.AppendLine("LEFT JOIN TradingBotDb.Financials.Securities AS s ON s.Symbol = sdc.Symbol");

            var parameters = new DynamicParameters();

            if (symbols != null && symbols.Any())
            {
                queryBuilder.AppendLine("WHERE sdc.Symbol IN @Symbols");
                parameters.Add("@Symbols", symbols);
            }

            queryBuilder.AppendLine("GROUP BY sdc.Symbol, sdc.Interval");
            queryBuilder.AppendLine("HAVING MAX(sdc.Year) - MIN(sdc.Year) > 15;");

            return (await connection.QueryAsync<SymbolCoverageBacktestModel>(queryBuilder.ToString(), parameters)).ToList();
        }

        public async Task<bool> UpdateSecuritiesOverview(int securityId, SecuritiesOverview securitiesOverview)
        {
            if (securitiesOverview.Symbol == null)
                return false;

            securitiesOverview.SecurityID = await GetSecurityId(securitiesOverview.Symbol);

            lock (_lock)
            {
                var connection = new SqlConnection(_connectionString);

                var insertQuery = @"
                    INSERT INTO [TradingBotDb].[Financials].[SecuritiesOverview]
                    (
                        SecurityID,
                        Symbol,
                        Name,
                        Description,
                        CIK,
                        Exchange,
                        Currency,
                        Country,
                        Sector,
                        Industry,
                        Address,
                        OfficialSite,
                        FiscalYearEnd,
                        LatestQuarter,
                        MarketCapitalization,
                        EBITDA,
                        PERatio,
                        PEGRatio,
                        BookValue,
                        DividendPerShare,
                        DividendYield,
                        EPS,
                        RevenuePerShareTTM,
                        ProfitMargin,
                        OperatingMarginTTM,
                        ReturnOnAssetsTTM,
                        ReturnOnEquityTTM,
                        RevenueTTM,
                        GrossProfitTTM,
                        DilutedEPSTTM,
                        QuarterlyEarningsGrowthYOY,
                        QuarterlyRevenueGrowthYOY,
                        AnalystTargetPrice,
                        AnalystRatingStrongBuy,
                        AnalystRatingBuy,
                        AnalystRatingHold,
                        AnalystRatingSell,
                        AnalystRatingStrongSell,
                        TrailingPE,
                        ForwardPE,
                        PriceToSalesRatioTTM,
                        PriceToBookRatio,
                        EVToRevenue,
                        EVToEBITDA,
                        Beta,
                        FiftyTwoWeekHigh,
                        FiftyTwoWeekLow,
                        FiftyDayMovingAverage,
                        TwoHundredDayMovingAverage,
                        SharesOutstanding,
                        DividendDate,
                        ExDividendDate,
                        LastUpdated
                    )
                    VALUES
                    (
                        @SecurityID,
                        @Symbol,
                        @Name,
                        @Description,
                        @CIK,
                        @Exchange,
                        @Currency,
                        @Country,
                        @Sector,
                        @Industry,
                        @Address,
                        @OfficialSite,
                        @FiscalYearEnd,
                        @LatestQuarter,
                        @MarketCapitalization,
                        @EBITDA,
                        @PERatio,
                        @PEGRatio,
                        @BookValue,
                        @DividendPerShare,
                        @DividendYield,
                        @EPS,
                        @RevenuePerShareTTM,
                        @ProfitMargin,
                        @OperatingMarginTTM,
                        @ReturnOnAssetsTTM,
                        @ReturnOnEquityTTM,
                        @RevenueTTM,
                        @GrossProfitTTM,
                        @DilutedEPSTTM,
                        @QuarterlyEarningsGrowthYOY,
                        @QuarterlyRevenueGrowthYOY,
                        @AnalystTargetPrice,
                        @AnalystRatingStrongBuy,
                        @AnalystRatingBuy,
                        @AnalystRatingHold,
                        @AnalystRatingSell,
                        @AnalystRatingStrongSell,
                        @TrailingPE,
                        @ForwardPE,
                        @PriceToSalesRatioTTM,
                        @PriceToBookRatio,
                        @EVToRevenue,
                        @EVToEBITDA,
                        @Beta,
                        @FiftyTwoWeekHigh,
                        @FiftyTwoWeekLow,
                        @FiftyDayMovingAverage,
                        @TwoHundredDayMovingAverage,
                        @SharesOutstanding,
                        @DividendDate,
                        @ExDividendDate,
                        @LastUpdated
                    )";

                return connection.Execute(insertQuery, new
                {
                    securitiesOverview.SecurityID,
                    securitiesOverview.Symbol,
                    securitiesOverview.Name,
                    securitiesOverview.Description,
                    securitiesOverview.CIK,
                    securitiesOverview.Exchange,
                    securitiesOverview.Currency,
                    securitiesOverview.Country,
                    securitiesOverview.Sector,
                    securitiesOverview.Industry,
                    securitiesOverview.Address,
                    securitiesOverview.OfficialSite,
                    securitiesOverview.FiscalYearEnd,
                    securitiesOverview.LatestQuarter,
                    securitiesOverview.MarketCapitalization,
                    securitiesOverview.EBITDA,
                    securitiesOverview.PERatio,
                    securitiesOverview.PEGRatio,
                    securitiesOverview.BookValue,
                    DividendPerShare = securitiesOverview.ParsedDividendPerShare,
                    DividendYield = securitiesOverview.ParsedDividendYield,
                    securitiesOverview.EPS,
                    securitiesOverview.RevenuePerShareTTM,
                    securitiesOverview.ProfitMargin,
                    securitiesOverview.OperatingMarginTTM,
                    securitiesOverview.ReturnOnAssetsTTM,
                    securitiesOverview.ReturnOnEquityTTM,
                    securitiesOverview.RevenueTTM,
                    securitiesOverview.GrossProfitTTM,
                    securitiesOverview.DilutedEPSTTM,
                    securitiesOverview.QuarterlyEarningsGrowthYOY,
                    securitiesOverview.QuarterlyRevenueGrowthYOY,
                    securitiesOverview.AnalystTargetPrice,
                    securitiesOverview.AnalystRatingStrongBuy,
                    securitiesOverview.AnalystRatingBuy,
                    securitiesOverview.AnalystRatingHold,
                    securitiesOverview.AnalystRatingSell,
                    securitiesOverview.AnalystRatingStrongSell,
                    securitiesOverview.TrailingPE,
                    securitiesOverview.ForwardPE,
                    securitiesOverview.PriceToSalesRatioTTM,
                    securitiesOverview.PriceToBookRatio,
                    securitiesOverview.EVToRevenue,
                    securitiesOverview.EVToEBITDA,
                    securitiesOverview.Beta,
                    securitiesOverview.FiftyTwoWeekHigh,
                    securitiesOverview.FiftyTwoWeekLow,
                    securitiesOverview.FiftyDayMovingAverage,
                    securitiesOverview.TwoHundredDayMovingAverage,
                    securitiesOverview.SharesOutstanding,
                    DividendDate = securitiesOverview.ParsedDividendDate,
                    ExDividendDate = securitiesOverview.ParsedExDividendDate,
                    LastUpdated = securitiesOverview.ParsedLastUpdated
                }) > 0;
            }
        }

        public async Task<SecuritiesOverview> GetSecurityOverview(string symbol)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT * FROM [TradingBotDb].[Financials].[SecuritiesOverview] WHERE Symbol = @Symbol";
            return await connection.QueryFirstOrDefaultAsync<SecuritiesOverview>(query, new { Symbol = symbol });
        }

        public async Task<int> GetSecurityId(string symbol)
        {
            if (symbol == null)
                return 0;
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT SecurityID FROM [TradingBotDb].[Financials].[Securities] WHERE Symbol = @Symbol";
            var securityId = await connection.ExecuteScalarAsync<int>(query, new { Symbol = symbol });

            if (securityId == 0)
                {
                var insertQuery = @"
                    INSERT INTO [TradingBotDb].[Financials].[Securities] (Symbol) 
                    VALUES (@Symbol); 
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";
                securityId = await connection.ExecuteScalarAsync<int>(insertQuery, new { Symbol = symbol });
            }

            return securityId;
        }

        public async Task<bool> DeleteSecurity(string symbol)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "DELETE FROM [TradingBotDb].[Financials].[Securities] WHERE Symbol = @Symbol";
            return await connection.ExecuteAsync(query, new { Symbol = symbol }) > 0;
        }
    }
}
