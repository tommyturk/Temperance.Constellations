using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;

namespace Temperance.Data.Data.Repositories.WalkForward.Implementations
{
    public class WalkForwardRepository : IWalkForwardRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<WalkForwardRepository> _logger;

        public WalkForwardRepository(IConfiguration configuration, ILogger<WalkForwardRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
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

        public async Task<IEnumerable<WalkForwardSleeve>> GetActiveSleeveAsync(Guid sessionId, DateTime asOfDate)
        {
            const string sql = @"
                WITH RankedSleeves AS (
                    SELECT 
                        *,
                        ROW_NUMBER() OVER(PARTITION BY Symbol ORDER BY TradingPeriodStartDate DESC) as rn
                    FROM [Constellations].[WalkForwardSleeves]
                    WHERE SessionId = @SessionId 
                      AND TradingPeriodStartDate <= @AsOfDate
                )
                SELECT * FROM RankedSleeves WHERE rn = 1 AND IsActive = 1;";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<WalkForwardSleeve>(sql, new { SessionId = sessionId, AsOfDate = asOfDate });
        }
    }
}