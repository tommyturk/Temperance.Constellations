using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using Temperance.Data.Data.Repositories.Indicator.Interfaces;
using Temperance.Data.Models.MarketHealth;

namespace Temperance.Data.Data.Repositories.Indicator.Implementation
{
    public class IndicatorRepository : IIndicatorRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<IndicatorRepository> _logger;

        public IndicatorRepository(string connectionString, ILogger<IndicatorRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        public async Task<decimal?> GetLatestIndicatorValue(string indicatorTableName, DateTime asOfDate)
        {
            using var connection = CreateConnection();
            var sql = $@"
                SELECT TOP 1 [Value]
                FROM [TradingBotDb].[Indicators].[{indicatorTableName}]
                WHERE [Date] <= @AsOfDate
                ORDER BY [Date] DESC;";
            try
            {
                return await connection.QuerySingleOrDefaultAsync<decimal?>(sql, new { AsOfDate = asOfDate });
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Could not find or query table for indicator: {IndicatorName}", indicatorTableName);
                return null;
            }
        }

        public async Task<List<IndicatorValue>> GetRecentIndicatorValues(string indicatorTableName, DateTime asOfDate, int count)
        {
            using var connection = CreateConnection();
            var sql = $@"
                SELECT TOP (@Count) [Date], [Value]
                FROM [TradingBotDb].[Indicators].[{indicatorTableName}]
                WHERE [Date] <= @AsOfDate
                ORDER BY [Date] DESC;";
            try
            {
                var results = await connection.QueryAsync<IndicatorValue>(sql, new { Count = count, AsOfDate = asOfDate });
                return results.Reverse().ToList(); 
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Could not find or query table for indicator: {IndicatorName}", indicatorTableName);
                return new List<IndicatorValue>();
            }
        }

        public async Task<decimal?> GetTreasuryYieldOnDate(string maturity, DateTime asOfDate)
        {
            using var connection = CreateConnection();
            const string sql = @"
                SELECT TOP 1 [Value]
                FROM [TradingBotDb].[Indicators].[Treasury_Yields]
                WHERE [Date] <= @AsOfDate AND [Maturity] = @Maturity
                ORDER BY [Date] DESC;";
            return await connection.QuerySingleOrDefaultAsync<decimal?>(sql, new { AsOfDate = asOfDate, Maturity = maturity });
        }
    }
}
