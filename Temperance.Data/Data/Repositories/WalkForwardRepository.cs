using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
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

        public async Task<HashSet<string>> GetSleeveSymbolsForPeriodAsync(Guid sessionId, DateTime tradingPeriodStartDate)
        {
            const string sql = @"
                SELECT Symbol 
                FROM [Constellations].[WalkForwardSleeves]
                WHERE SessionId = @SessionId 
                  AND TradingPeriodStartDate = @TradingPeriodStartDate;";

            await using var connection = new SqlConnection(_connectionString);
            var symbols = await connection.QueryAsync<string>(sql, new { SessionId = sessionId, TradingPeriodStartDate = tradingPeriodStartDate });
            return new HashSet<string>(symbols);
        }

        public async Task UpdateSessionCapitalAsync(Guid sessionId, double? finalCapital)
        {
            const string sql = @"
                UPDATE [Constellations].[WalkForwardSessions]
                SET CurrentCapital = @FinalCapital
                WHERE SessionId = @SessionId;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { FinalCapital = finalCapital, SessionId = sessionId });
            return;
        }

        public async Task<IEnumerable<OptimizationJob>> GetCompletedJobsForSessionAsync(Guid sessionId)
        {
            const string sql = @"
                SELECT OJ.*, SOP.Symbol, SOP.ResultKey, SOP.OptimizedParametersJson FROM [Conductor].[OptimizationJobs] AS OJ
                LEFT JOIN [Ludus].[StrategyOptimizedParameters] AS SOP
                ON OJ.JobId = SOP.JobId
                WHERE OJ.SessionId = @SessionId AND OJ.Status LIKE '%Completed%';";

            await using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<OptimizationJob>(sql, new { SessionId = sessionId });
        }

        public async Task<IEnumerable<StrategyOptimizedParameters>> GetResultsByKeysAsync(List<string> resultKeys, Guid sessionId)
        {
            const string sql = @"
                SELECT *
                FROM [Ludus].[StrategyOptimizedParameters]
                WHERE ResultKey IN @ResultKeys
                    AND SessionId = @SessionId;";

            await using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<StrategyOptimizedParameters>(sql, new { ResultKeys = resultKeys, SessionId = sessionId });
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

        public async Task<BacktestRun> GetLatestRunForSessionAsync(Guid sessionId)
        {
            var query = @"
                SELECT TOP 1 *
                FROM [Constellations].[BacktestRuns]
                WHERE SessionId = @SessionId
                ORDER BY StartTime DESC;";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<BacktestRun>(query, new { SessionId = sessionId });
        }

        public async Task<StrategyOptimizedParameters> GetOptimizedParametersForSymbol(Guid sessionId, string symbol, DateTime dateTime)
        {
            var query = @"
                SELECT TOP 1 *
                FROM [Ludus].[StrategyOptimizedParameters]
                WHERE SessionId = @SessionId
                  AND Symbol = @Symbol
                  AND CreatedAt <= @DateTime
                ORDER BY CreatedAt DESC;";
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<StrategyOptimizedParameters>(query, new { SessionId = sessionId, Symbol = symbol, DateTime = dateTime });
        }

        public async Task CreateCycleTracker(CycleTracker cycle)
        {
            const string sql = @"
                INSERT INTO [Constellations].[CycleTrackers]
                    (
                        CycleTrackerId, SessionId, 
                        CycleStartDate, OosStartDate, OosEndDate,
                        PortfolioBacktestRunId, ShadowBacktestRunId,
                        IsPortfolioBacktestComplete, IsShadowBacktestComplete, IsOptimizationDispatched,
                        CreatedAt)
                VALUES
                    (@CycleTrackerId, @SessionId, 
                    @CycleStartDate, @OosStartDate, @OosEndDate,
                    @PortfolioBacktestRunId, @ShadowBacktestRunId, 
                    @IsPortfolioBacktestComplete, @IsShadowBacktestComplete, @IsOptimizationDispatched,                    
                    @CreatedAt);";
            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, cycle);
        }

        public async Task<CycleTracker> GetCycleTrackerAsync(Guid cycleTrackerId)
        {
            const string sql = "SELECT * FROM [Constellations].[CycleTrackers] WHERE CycleTrackerId = @CycleTrackerId;";
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<CycleTracker>(sql, new { CycleTrackerId = cycleTrackerId });
        }

        public async Task<CycleTracker> SignalCompletionAndCheckIfReady(Guid cycleTrackerId, BacktestType backtestType)
        {
            var sql = @"
                UPDATE [Constellations].[CycleTrackers]
                SET 
                    IsPortfolioBacktestComplete = CASE WHEN @BacktestType = 0 THEN 1 ELSE IsPortfolioBacktestComplete END,
                    IsShadowBacktestComplete = CASE WHEN @BacktestType = 1 THEN 1 ELSE IsShadowBacktestComplete END
                OUTPUT INSERTED.*
                WHERE CycleTrackerId = @CycleTrackerId;";

            using var connection = new SqlConnection(_connectionString);

            // Dapper's QuerySingleOrDefaultAsync executes the command and maps the OUTPUT row back to our C# object.
            var updatedTracker = await connection.QuerySingleOrDefaultAsync<CycleTracker>(sql, new
            {
                CycleTrackerId = cycleTrackerId,
                BacktestType = (int)backtestType 
            });

            return updatedTracker;
        }

        public async Task MarkOptimizationAsDispatchedAsync(Guid cycleTrackerId)
        {
            const string sql = @"
                UPDATE [Constellations].[CycleTrackers]
                SET IsOptimizationDispatched = 1
                WHERE CycleTrackerId = @CycleTrackerId;";

            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { CycleTrackerId = cycleTrackerId });
        }

        public async Task<List<CycleTracker>> GetCycleTrackersForSession(Guid sessionId)
        {
            const string sql = @"
                SELECT *
                FROM [Constellations].[CycleTrackers]
                WHERE SessionId = @SessionId
                ORDER BY CycleStartDate DESC;";

            using var connection = new SqlConnection(_connectionString);
            var result = await connection.QueryAsync<CycleTracker>(sql, new { SessionId = sessionId } );

            return result.ToList();
        }

        public async Task UpdateCurrentCapital(Guid sessionId, decimal profitLoss)
        {
            const string sql = @"
                UPDATE [Constellations].[WalkForwardSessions]
                SET CurrentCapital = ISNULL(CurrentCapital, 0) + @ProfitLoss
                WHERE SessionId = @SessionId;";

            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { ProfitLoss = profitLoss, SessionId = sessionId });
            return;
        }

        public async Task<PortfolioState> GetLatestPortfolioStateAsync(Guid sessionId)
        { 
            const string query = @"
                SELECT *
                FROM [Constellations].[PortfolioStates]
                WHERE SessionId = @SessionId
                AND AsOfDate = (
                    SELECT MAX(AsOfDate)
                    FROM [Constellations].[PortfolioStates]
                    WHERE SessionId = @SessionId
                );";
            await using var connection = new SqlConnection(_connectionString);
            return (await connection.QueryAsync<PortfolioState>(query, new { SessionId = sessionId })).ToList();
        }
    }
}