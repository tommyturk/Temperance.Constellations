using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Trading;

namespace Temperance.Data.Data.Repositories.Trade.Implementations
{
    public class BacktestRepository : IBacktestRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<BacktestRepository> _logger;
        public BacktestRepository(string connectionString, ILogger<BacktestRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult result, decimal initialCapital)
        {
            const string query = @"
                UPDATE [TradingBotDb].[Constellations].[BacktestRuns]
                SET TotalTrades = @TotalTrades,
                    TotalProfitLoss = @TotalProfitLoss,
                    TotalReturn = @TotalReturn,
                    MaxDrawdown = @MaxDrawdown,
                    WinRate = @WinRate,
                    ErrorMessage = @ErrorMessage
                WHERE RunId = @RunId;";

            try
            {
                using var connection = CreateConnection();
                var parameters = new
                {
                    RunId = runId,
                    result.TotalTrades,
                    result.TotalProfitLoss,
                    result.TotalReturn,
                    result.MaxDrawdown,
                    result.WinRate,
                    result.ErrorMessage
                };
                int affectedRows = await connection.ExecuteAsync(query, parameters);
                _logger.LogInformation("Updated performance metrics for backtest run {RunId}.", runId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update performance metrics for backtest run {RunId}.", runId);
                throw;
            }
        }

        public async Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades)
        {
            const string sql = @"
                INSERT INTO Constellations.BacktestTrades (RunId, Symbol, Interval, StrategyName, EntryDate, ExitDate, EntryPrice, ExitPrice, Quantity, Direction, ProfitLoss)
                VALUES (@RunId, @Symbol, @Interval, @StrategyName, @EntryDate, @ExitDate, @EntryPrice, @ExitPrice, @Quantity, @Direction, @ProfitLoss);";
            try
            {
                using var connection = CreateConnection();
                int affectedRows = await connection.ExecuteAsync(sql, trades.Select(t => new
                {
                    t.RunId,
                    t.Symbol,
                    t.Interval,
                    t.StrategyName,
                    t.EntryDate,
                    t.ExitDate,
                    t.EntryPrice,
                    t.ExitPrice,
                    t.Quantity,
                    t.Direction,
                    t.ProfitLoss
                }));
                _logger.LogInformation("Saved {TradeCount} backtest trades for run {RunId}.", affectedRows, runId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save backtest trades for run {RunId}.", runId);
                throw;
            }
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}
