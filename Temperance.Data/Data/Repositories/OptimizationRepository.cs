using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Temperance.Data.Models.Backtest;

namespace Temperance.Data.Data.Repositories
{
    public class OptimizationRepository : IOptimizationRepository
    {
        private readonly string _connectionString;
        public OptimizationRepository(string connectionString)
        {
            _connectionString = connectionString;
        }
        public async Task<List<OptimizationJob>> GetOptimizationResultForSession(Guid sessionId, DateTime inSampleEndDate)
        {
            var query = $@"
                SELECT *
                FROM [TradingBotDb].[Conductor].[OptimizationJobs]
                WHERE SessionId = @SessionId
                  AND InSampleEndDate = @InSampleEndDate;"; 

            using var connection = new SqlConnection(_connectionString);
            var results = await connection.QueryAsync<OptimizationJob>(query, new { SessionId = sessionId, InSampleEndDate = inSampleEndDate });
            return results.ToList();
        }

        public async Task<IEnumerable<OptimizationResultDto>> GetOptimizationResultsByWindowAsync(string strategyName, string interval, DateTime startDate, DateTime endDate)
        {
            const string sql = @"
                SELECT 
                    Symbol
                    --,InSampleSharpe -- This column is required for ranking
                FROM [Ludus].[StrategyOptimizedParameters]
                WHERE StrategyName = @strategyName
                  AND Interval = @interval
                  AND StartDate = @startDate
                  AND EndDate = @endDate;
            ";

            await using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<OptimizationResultDto>(sql, new
            {
                strategyName,
                interval,
                startDate,
                endDate
            });
        }

        public async Task<Dictionary<string, Dictionary<string, object>>> GetOptimizationResultsBySymbolsAsync(string strategyName, string interval, DateTime startDate, DateTime endDate, List<string> symbols)
        {
            const string sql = @"
                SELECT 
                    Symbol,
                    OptimizedParametersJson
                    --,InSampleSharpe -- This column is required for ranking
                FROM [Ludus].[StrategyOptimizedParameters]
                WHERE StrategyName = @strategyName
                  AND Interval = @interval
                  AND StartDate = @startDate
                  AND EndDate = @endDate
                  AND Symbol IN @symbols;
            ";
            await using var connection = new SqlConnection(_connectionString);
            var results = await connection.QueryAsync(sql, new
            {
                strategyName,
                interval,
                startDate,
                endDate,
                symbols
            });

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
    }
}
