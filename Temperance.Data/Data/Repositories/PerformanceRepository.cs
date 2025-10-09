using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using Temperance.Data.Models.Backtest;

namespace Temperance.Data.Data.Repositories
{
    public class PerformanceRepository : IPerformanceRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<PerformanceRepository> _logger;

        public PerformanceRepository(string connectionString, ILogger<PerformanceRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task<IEnumerable<SleeveComponent>> GetSleeveComponentsAsync(Guid runId)
        {
            const string sql = @"
                SELECT * FROM [Constellations].[SleeveComponents] 
                WHERE RunId = @RunId;";

            await using var _dbConnection = new SqlConnection(_connectionString);
            return await _dbConnection.QueryAsync<SleeveComponent>(sql, new { RunId = runId });
        }

        public async Task<IEnumerable<ShadowPerformance>> GetShadowPerformanceAsync(Guid runId)
        {
            const string sql = @"
                SELECT * FROM [Constellations].[ShadowPerformance] 
                WHERE RunId = @RunId;";
            await using var _dbConnection = new SqlConnection(_connectionString);
            return await _dbConnection.QueryAsync<ShadowPerformance>(sql, new { RunId = runId });
        }

        public async Task SaveSleeveComponentsAsync(IEnumerable<SleeveComponent> components)
        {
            const string sql = @"
                INSERT INTO [Constellations].[SleeveComponents] 
                (SleeveComponentId, RunId, SessionId, Symbol, SharpeRatio, ProfitLoss, TotalTrades, WinRate)
                VALUES 
                (@SleeveComponentId, @RunId, @SessionId, @Symbol, @SharpeRatio, @ProfitLoss, @TotalTrades, @WinRate);";
            await using var _dbConnection = new SqlConnection(_connectionString);
            await _dbConnection.ExecuteAsync(sql, components);
        }

        public async Task SaveShadowPerformanceAsync(IEnumerable<ShadowPerformance> performances)
        {
            const string sql = @"
                INSERT INTO [Constellations].[ShadowPerformance] 
                (RunId, Symbol, SharpeRatio, ProfitLoss, TotalTrades, WinRate)
                VALUES 
                (@RunId, @Symbol, @SharpeRatio, @ProfitLoss, @TotalTrades, @WinRate);";
            await using var _dbConnection = new SqlConnection(_connectionString);
            await _dbConnection.ExecuteAsync(sql, performances);
        }
    }
}
