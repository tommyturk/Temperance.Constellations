using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Temperance.Data.Data.Repositories.Securities.Interfaces;
using Temperance.Data.Models.HistoricalData;

namespace Temperance.Data.Repositories.Securities.Implementations
{
    public class SecuritiesOverviewRepository : ISecuritiesOverviewRepository
    {
        private readonly string _connectionString;
        private static readonly object _lock = new object();
        private ILogger<SecuritiesOverviewRepository> _logger;

        public SecuritiesOverviewRepository(string connectionString, ILogger<SecuritiesOverviewRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;

            if (string.IsNullOrWhiteSpace(_connectionString))
                _logger.LogCritical("FATAL: SecuritiesOverviewRepository created with a NULL or EMPTY connection string!");
            else
            {
                var builder = new SqlConnectionStringBuilder(_connectionString);
                _logger.LogInformation("SecuritiesOverviewRepository instance created.");
                _logger.LogInformation("Attempting to connect to SERVER: [{Server}], DATABASE: [{Database}]", builder.DataSource, builder.InitialCatalog);
            }
        }

        public async Task<List<string>> GetSecurities()
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT Symbol FROM [TradingBotDb].[Financials].[Securities]";
            return (await connection.QueryAsync<string>(query)).ToList();
        }

        public async IAsyncEnumerable<SymbolCoverageBacktestModel> StreamSecuritiesForBacktest(
            List<string> symbols,
            List<string> intervals,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var finalIntervals = intervals;
            if (finalIntervals == null || !finalIntervals.Any())
                finalIntervals = new List<string>() { "1min", "5min", "15min", "60min", "1d" };

            if (symbols != null && symbols.Any())
            {
                foreach (var symbol in symbols)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    foreach (var interval in finalIntervals)
                    {
                        yield return new SymbolCoverageBacktestModel { Symbol = symbol, Interval = interval };
                    }
                }
            }
            else
            {
                _logger.LogInformation("Streaming from database: Attempting to connect with connection string: {ConnectionString}", _connectionString);
                await using var connection = new SqlConnection(_connectionString);

                try
                {
                    await connection.OpenAsync(cancellationToken);
                    _logger.LogInformation("Database connection opened successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "FATAL: Failed to open database connection. The backtest cannot proceed.");
                    yield break;
                }

                const string query = "SELECT Symbol FROM [TradingBotDb].[Financials].[SecuritiesOverview] WHERE MarketCapitalization > 10000000000";
                await using var command = new SqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                int symbolsRead = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    symbolsRead++;
                    var symbol = reader.GetString(0);
                    _logger.LogDebug("Read symbol from database: {Symbol}", symbol);

                    foreach (var interval in finalIntervals)
                    {
                        yield return new SymbolCoverageBacktestModel
                        {
                            Symbol = symbol,
                            Interval = interval
                        };
                    }
                }

                _logger.LogInformation("Finished reading from database. Total symbols read that meet criteria: {SymbolCount}", symbolsRead);
            }
        }

        [Obsolete]
        public async Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbols = null, List<string> intervals = null)
        {
            using var connection = new SqlConnection(_connectionString);

            if(symbols == null || !symbols.Any())
                symbols = (await connection.QueryAsync<string>("SELECT DISTINCT Symbol FROM [TradingBotDb].[Financials].[SecuritiesOverview] WHERE MarketCapitalization > 10000000000")).ToList();

            if(intervals == null || !intervals.Any())
                intervals = new List<string>() { "1min", "5min", "15min", "60min", "1d" };

            List<SymbolCoverageBacktestModel> backtestModel = new List<SymbolCoverageBacktestModel>();

            foreach(var symbol in symbols)
            {
                foreach(var interval in intervals)
                {
                    var backtestItem = new SymbolCoverageBacktestModel()
                    {
                        Symbol = symbol,
                        Interval = interval
                    };

                    backtestModel.Add(backtestItem);
                }
            }
            return backtestModel;
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
