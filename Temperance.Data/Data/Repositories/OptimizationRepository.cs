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

        public async Task<IEnumerable<OptimizationResultDto>> GetOptimizationResultsByWindowAsync(
            string strategyName, string interval, DateTime startDate, DateTime endDate)
        {
            const string sql = @"
                SELECT 
                    Symbol,
                    Metrics 
                FROM [Ludus].[StrategyOptimizedParameters]
                WHERE StrategyName = @strategyName
                  AND Interval = @interval
                  AND StartDate = @startDate
                  AND EndDate = @endDate;
            ";

            await using var connection = new SqlConnection(_connectionString);

            var results = await connection.QueryAsync<dynamic>(sql, new
            {
                strategyName,
                interval,
                startDate,
                endDate
            });

            var optimizationResults = new List<OptimizationResultDto>();

            foreach (var row in results)
            {
                string metricsJson = row.Metrics as string;

                if (!string.IsNullOrEmpty(metricsJson))
                {
                    var metrics = JsonSerializer.Deserialize<OptimizationMetrics>(
                        metricsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    var optimizationResult = new OptimizationResultDto
                    {
                        Symbol = row.Symbol,
                        Metrics = metrics
                    };

                    optimizationResults.Add(optimizationResult);
                }
            }

            return optimizationResults;
        }

        public async Task<Dictionary<string, Dictionary<string, object>>> GetOptimizationResultsBySymbolsAsync(string strategyName, string interval, DateTime startDate, DateTime endDate, List<string> symbols)
        {
            const string sql = @"
                SELECT 
                    Symbol,
                    OptimizedParametersJson
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

        public async Task<List<StrategyOptimizedParameters>> GetLatestParametersAsync(
            string strategyName,
            List<string> symbols,
            string interval,
            DateTime pointInTime)
        {
            const string sql = @"
                ;WITH RankedParameters AS (
                    SELECT
                        [Id],
                        [StrategyName],
                        [Symbol],
                        [Interval],
                        [OptimizedParametersJson],
                        [CreatedAt],
                        -- 1. Create a ranked list for each symbol, ordered by most recent
                        ROW_NUMBER() OVER(
                            PARTITION BY [Symbol] 
                            ORDER BY [CreatedAt] DESC
                        ) AS RowNum
                    FROM 
                        [Ludus].[StrategyOptimizedParameters]
                    WHERE
                        -- 2. Filter by the inputs
                        [StrategyName] = @StrategyName
                        AND [Interval] = @Interval
                        AND [Symbol] IN @Symbols
                        -- 3. This is the crucial Point-in-Time (PIT) check
                        AND [CreatedAt] < @PointInTime 
                )
                -- 4. Select only the #1 (most recent) row for each symbol
                SELECT
                    [Id],
                    [StrategyName],
                    [Symbol],
                    [Interval],
                    [OptimizedParametersJson],
                    [CreatedAt]
                FROM 
                    RankedParameters
                WHERE 
                    RowNum = 1;
                ";
            using (var connection = new SqlConnection(_connectionString))
            {
                var results = await connection.QueryAsync<StrategyOptimizedParameters>(sql, new
                {
                    StrategyName = strategyName,
                    Symbols = symbols,
                    Interval = interval,
                    PointInTime = pointInTime
                });
                return results.ToList();
            }
        }
    }
}
