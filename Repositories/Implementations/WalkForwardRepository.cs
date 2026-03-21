using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Temperance.Constellations.Repositories.Interfaces;
using Temperance.Constellations.Models;
using Temperance.Ephemeris.Models.Backtesting;
using Temperance.Ephemeris.Models.Constellations;

namespace Temperance.Constellations.Repositories.Interfaces.WalkForward.Implementations
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

        public async Task<WalkForwardSessionModel> GetSessionAsync(Guid sessionId)
        {
            const string sql = "SELECT * FROM [Constellations].[WalkForwardSessions] WHERE SessionId = @SessionId;";
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<WalkForwardSessionModel>(sql, new { SessionId = sessionId });
        }

        public async Task UpdateSessionStatusAsync(Guid sessionId, string status)
        {
            const string sql = "UPDATE [Constellations].[WalkForwardSessions] SET Status = @Status WHERE SessionId = @SessionId;";
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { Status = status, SessionId = sessionId });
            _logger.LogInformation("Updated status for SessionId {SessionId} to '{Status}'", sessionId, status);
        }

        public async Task UpdateSessionCapitalAsync(Guid sessionId, decimal? finalCapital)
        {
            const string sql = @"
                UPDATE [Constellations].[WalkForwardSessions]
                SET CurrentCapital = @FinalCapital
                WHERE SessionId = @SessionId;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { FinalCapital = finalCapital, SessionId = sessionId });
            return;
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

        public async Task<BacktestRunModel> GetLatestRunForSessionAsync(Guid sessionId)
        {
            var query = @"
                SELECT TOP 1 *
                FROM [Constellations].[BacktestRuns]
                WHERE SessionId = @SessionId
                ORDER BY StartTime DESC;";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<BacktestRunModel>(query, new { SessionId = sessionId });
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
            var result = await connection.QuerySingleOrDefaultAsync<PortfolioState>(query, new { SessionId = sessionId });

            return result;
        }

        public async Task SaveCycleResultsAsync(Guid sessionId, Guid cycleRunId, BacktestResult cycleResult, PortfolioStateModel portfolioState)
        {


        }

        public async Task SaveCycleFailureAsync(Guid sessionId, Guid cycleId, string errorMessage)
        {
            const string sql = @"
            INSERT INTO [Constellations].[WalkForwardCycleFailures] 
                (Id, SessionId, CycleId, ErrorMessage, FailedAt)
            VALUES 
                (@Id, @SessionId, @CycleId, @ErrorMessage, @FailedAt);";

            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                CycleId = cycleId,
                ErrorMessage = errorMessage,
                FailedAt = DateTime.UtcNow
            });
        }
    }
}