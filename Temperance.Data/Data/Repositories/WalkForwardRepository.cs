using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;

namespace Temperance.Data.Data.Repositories.WalkForward.Implementations
{
    public class WalkForwardRepository : IWalkForwardRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<WalkForwardRepository> _logger;

        public WalkForwardRepository(string connectionString, ILogger<WalkForwardRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task<WalkForwardSession> GetSessionAsync(Guid sessionId)
        {
            const string sql = "SELECT * FROM [Constellations].[WalkForwardSessions] WHERE SessionId = @SessionId;";
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<WalkForwardSession>(sql, new { SessionId = sessionId });
        }

        public async Task UpdateSessionStatusAsync(Guid sessionId, string status)
        {
            const string sql = "UPDATE [Constellations].[WalkForwardSessions] SET Status = @Status WHERE SessionId = @SessionId;";
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { Status = status, SessionId = sessionId });
            _logger.LogInformation("Updated status for SessionId {SessionId} to '{Status}'", sessionId, status);
        }

        public async Task<IEnumerable<WalkForwardSleeve>> GetSleevesByBatchAsync(Guid sessionId, DateTime tradingPeriodStartDate)
        {
            const string sql = "SELECT * FROM [Constellations].[WalkForwardSleeves] WHERE SessionId = @SessionId AND TradingPeriodStartDate = @TradingPeriodStartDate;";
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<WalkForwardSleeve>(sql, new { SessionId = sessionId, TradingPeriodStartDate = tradingPeriodStartDate });
        }

            public async Task<IEnumerable<OptimizationJob>> GetCompletedJobsForSessionAsync(Guid sessionId)
            {
                // Make sure your OptimizationJobs table has a Symbol column!
                const string sql = @"
                    SELECT OJ.*, SOP.Symbol, SOP.ResultKey FROM [Conductor].[OptimizationJobs] AS OJ
                        LEFT JOIN [Ludus].[StrategyOptimizedParameters] AS SOP 
                        ON OJ.JobId = SOP.JobId
                    WHERE OJ.SessionId = @SessionId AND OJ.Status LIKE '%Completed%';";

                await using var connection = new SqlConnection(_connectionString);
                return await connection.QueryAsync<OptimizationJob>(sql, new { SessionId = sessionId });
            }

        public async Task<IEnumerable<StrategyOptimizedParameters>> GetResultsByKeysAsync(List<string> resultKeys)
        {
            const string sql = @"
                SELECT *
                FROM [Ludus].[StrategyOptimizedParameters]
                WHERE ResultKey IN @ResultKeys;";

            await using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<StrategyOptimizedParameters>(sql, new { ResultKeys = resultKeys });
        }

        public async Task CreateSleeveBatchAsync(List<WalkForwardSleeve> sleeves)
        {
            const string sql = @"
                INSERT INTO [Constellations].[WalkForwardSleeves]
                    (SessionId, TradingPeriodStartDate, Symbol, Interval, StrategyName, OptimizedParametersJson, OptimizationResultId, IsActive, CreatedAt)
                VALUES
                    (@SessionId, @TradingPeriodStartDate, @Symbol, @Interval, @StrategyName, @OptimizedParametersJson, @OptimizationResultId, @IsActive, @CreatedAt);";

            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, sleeves);
        }

        public async Task SetActiveSleeveAsync(Guid sessionId, DateTime tradingPeriodStartDate, IEnumerable<string> symbols)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                const string deactivateSql = @"
                    UPDATE [Constellations].[WalkForwardSleeves] 
                    SET IsActive = 0 
                    WHERE SessionId = @SessionId AND TradingPeriodStartDate = @TradingPeriodStartDate;";
                await connection.ExecuteAsync(deactivateSql, new { SessionId = sessionId, TradingPeriodStartDate = tradingPeriodStartDate }, transaction);

                const string activateSql = @"
                    UPDATE [Constellations].[WalkForwardSleeves] 
                    SET IsActive = 1 
                    WHERE SessionId = @SessionId AND TradingPeriodStartDate = @TradingPeriodStartDate AND Symbol IN @Symbols;";
                await connection.ExecuteAsync(activateSql, new { SessionId = sessionId, TradingPeriodStartDate = tradingPeriodStartDate, Symbols = symbols }, transaction);

                transaction.Commit();
                _logger.LogInformation("Set active sleeve for SessionId {SessionId} with {Count} symbols.", sessionId, symbols.Count());
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed to set active sleeve for SessionId {SessionId}", sessionId);
                throw;
            }
        }

        public async Task<Dictionary<string, Dictionary<string, object>>> GetLatestParametersForSleeveAsync(Guid sessionId, IEnumerable<string> symbols)
        {
            // This SQL query uses a Common Table Expression (CTE) and the ROW_NUMBER() window function.
            // It partitions the data by Symbol and orders it by creation date descending,
            // ensuring we get only the single most recent optimization result for each symbol within the session.
            const string sql = @"
                WITH RankedResults AS (
                    SELECT
                        Symbol,
                        OptimizedParametersJson,
                        ROW_NUMBER() OVER(PARTITION BY Symbol ORDER BY CreatedAt DESC) as rn
                    FROM
                        Ludus.StrategyOptimizedParameters
                    WHERE
                        SessionId = @SessionId AND Symbol IN @Symbols
                )
                SELECT
                    Symbol,
                    OptimizedParametersJson
                FROM
                    RankedResults
                WHERE
                rn = 1;";

            await using var connection = new SqlConnection(_connectionString);
            var results = await connection.QueryAsync(sql, new { SessionId = sessionId, Symbols = symbols });

            var parametersBySymbol = new Dictionary<string, Dictionary<string, object>>();
            if (results == null) return parametersBySymbol;

            foreach (var row in results)
            {
                if (!string.IsNullOrEmpty(row.OptimizedParametersJson))
                {
                    var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(row.OptimizedParametersJson);
                    parametersBySymbol[row.Symbol] = parameters;
                }
            }

            return parametersBySymbol;
        }

        public async Task<IEnumerable<WalkForwardSleeve>> GetActiveSleeveAsync(Guid sessionId, DateTime asOfDate)
        {
            // This SQL is correct. It finds the most recent sleeve record for each symbol
            // on or before the given date and filters for the ones that are marked as active.
            // If this fails, the issue is with the _connectionString not pointing to TradingBotDb.
            const string sql = @"
                WITH RankedSleeves AS (
                    SELECT 
                        SleeveId,
                        SessionId,
                        TradingPeriodStartDate,
                        Symbol,
                        Interval,
                        OptimizationResultId,
                        InSampleSharpeRatio,
                        InSampleMaxDrawdown,
                        OptimizedParametersJson,
                        IsActive,
                        CreatedAt,
                        StrategyName,
                        ROW_NUMBER() OVER(PARTITION BY Symbol ORDER BY TradingPeriodStartDate DESC) as rn
                    FROM [Constellations].[WalkForwardSleeves]
                    WHERE SessionId = @SessionId 
                      AND TradingPeriodStartDate <= @AsOfDate
                )
                SELECT 
                    SleeveId,
                    SessionId,
                    TradingPeriodStartDate,
                    Symbol,
                    Interval,
                    OptimizationResultId,
                    InSampleSharpeRatio,
                    InSampleMaxDrawdown,
                    OptimizedParametersJson,
                    IsActive,
                    CreatedAt,
                    StrategyName
                FROM RankedSleeves 
                WHERE rn = 1 AND IsActive = 1;";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<WalkForwardSleeve>(sql, new { SessionId = sessionId, AsOfDate = asOfDate });
        }
    }
}