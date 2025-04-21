using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces;
using TradingBot.Data.Data.Repositories.Trade.Interfaces;
using TradingBot.Data.Models.Backtest;
using TradingBot.Data.Models.Trading;

namespace TradingBot.Data.Data.Repositories.Trade.Implementations
{
    public class TradeRepository : ITradeRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<TradeRepository> _logger;

        // Updated Constructor
        public TradeRepository(string connectionString, ILogger<TradeRepository> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger;
        }

        private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

        // --- Existing Methods (Keep as they are) ---
        public async Task<int> SaveTradeAsync(Models.Trading.Trade trade)
        {
            // ... your existing implementation ...
            using var connection = new SqlConnection(_connectionString);
            string query = @"
            INSERT INTO [Trading].[Trades]
            (SecurityID, Symbol, Strategy, TradeType, SignalPrice, SignalTimestamp, Status, TakeProfitPrice, StopLossPrice)
            VALUES (@SecurityID, @Symbol, @Strategy, @TradeType, @SignalPrice, @SignalTimestamp, @Status, @TakeProfitPrice, @StopLossPrice);
            SELECT SCOPE_IDENTITY();"; // Added TP/SL based on CheckTradeExits
            try { return await connection.ExecuteScalarAsync<int>(query, trade); }
            catch (Exception ex) { _logger.LogError(ex, "Error saving live/sim trade for Symbol {Symbol}", trade?.Symbol); throw; }
        }

        public async Task<int> ExecuteOrderAsync(Models.Trading.Order order)
        {
            // ... your existing implementation ...
            using var connection = new SqlConnection(_connectionString);
            string query = @"
            INSERT INTO [Trading].[Orders]
            (TradeID, ExecutionPrice, ExecutionTimestamp, OrderStatus, Quantity)
            VALUES (@TradeID, @ExecutionPrice, @ExecutionTimestamp, @OrderStatus, @Quantity);
            SELECT SCOPE_IDENTITY();";
            try { return await connection.ExecuteScalarAsync<int>(query, order); }
            catch (Exception ex) { _logger.LogError(ex, "Error executing order for TradeID {TradeID}", order?.TradeID); throw; }
        }

        public async Task<int> UpdatePositionAsync(Models.Trading.Position position)
        {
            // ... your existing implementation ...
            // WARNING: This MERGE logic for average price might be inaccurate for complex scenarios (e.g., shorting, partial closes)
            // Consider simpler updates or a dedicated position management component if live trading is complex.
            using var connection = new SqlConnection(_connectionString);
            string query = @"
            MERGE INTO [Trading].[Positions] AS target
            USING (SELECT @SecurityID AS SecurityID, @Symbol AS Symbol) AS source
            ON target.SecurityID = source.SecurityID AND target.Symbol = source.Symbol -- Match on Symbol too?
            WHEN MATCHED AND target.Quantity + @Quantity <> 0 THEN -- Update existing position
                UPDATE SET
                    AveragePrice = CASE
                                        WHEN target.Quantity + @Quantity = 0 THEN 0 -- Avoid division by zero if position closes
                                        WHEN SIGN(target.Quantity) = SIGN(@Quantity) THEN -- Adding to position
                                            (target.AveragePrice * target.Quantity + @AveragePrice * @Quantity) / (target.Quantity + @Quantity)
                                        ELSE -- Reducing position (use entry price of reduction for avg calculation?) - Complex! This needs review.
                                            target.AveragePrice -- Keeping original avg price when reducing? Or recalculate? Safer to keep for now.
                                    END,
                    Quantity = target.Quantity + @Quantity,
                    Status = CASE WHEN target.Quantity + @Quantity = 0 THEN 'Closed' ELSE target.Status END -- Close if quantity is zero
            WHEN MATCHED AND target.Quantity + @Quantity = 0 THEN -- Closing position completely
                UPDATE SET
                    Quantity = 0,
                    AveragePrice = 0,
                    Status = 'Closed',
                    UnrealizedPL = 0 -- Reset PL
            WHEN NOT MATCHED AND @Quantity <> 0 THEN -- Insert new position
                INSERT (SecurityID, Symbol, Quantity, AveragePrice, UnrealizedPL, Status)
                VALUES (@SecurityID, @Symbol, @Quantity, @AveragePrice, 0, 'Open'); -- Initial UnrealizedPL is 0
             "; // Note: Added more conditions, AveragePrice logic needs careful review for buys/sells/shorts.
            try { return await connection.ExecuteAsync(query, position); }
            catch (Exception ex) { _logger.LogError(ex, "Error updating position for SecurityID {SecurityID}", position?.SecurityID); throw; }
        }

        public async Task<int> LogStrategyAsync(StrategyLog log)
        {
            // ... your existing implementation ...
            using var connection = new SqlConnection(_connectionString);
            string query = @"
            INSERT INTO [Trading].[StrategyLogs]
            (TradeID, MovingAverage, StandardDeviation, UpperThreshold, LowerThreshold, Reason, CreatedAt)
            VALUES (@TradeID, @MovingAverage, @StandardDeviation, @UpperThreshold, @LowerThreshold, @Reason, @CreatedAt);";
            try { return await connection.ExecuteAsync(query, log); }
            catch (Exception ex) { _logger.LogError(ex, "Error logging strategy details for TradeID {TradeID}", log?.TradeID); throw; }
        }

        public async Task CheckTradeExitsAsync()
        {
            // ... your existing implementation ...
            // This logic remains tied to the live/sim 'Trades' table and IHistoricalPriceRepository
            // It won't be used directly by the backtesting result saving process.
            using var connection = new SqlConnection(_connectionString);
            // ... (rest of your existing implementation) ...
        }

        // --- New Backtesting Method Implementations ---

        public async Task InitializeBacktestRunAsync(Guid runId, BacktestConfiguration config)
        {
            const string sql = @"
                INSERT INTO  [TradingBotDb].[Constellations].[BacktestRuns] (RunId, StrategyName, ParametersJson, SymbolsJson, IntervalsJson, StartDate, EndDate, InitialCapital, Status, StartTime)
                VALUES (@RunId, @StrategyName, @ParametersJson, @SymbolsJson, @IntervalsJson, @StartDate, @EndDate, @InitialCapital, 'Queued', @StartTime);";
            try
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(sql, new
                {
                    RunId = runId,
                    config.StrategyName,
                    ParametersJson = JsonSerializer.Serialize(config.StrategyParameters),
                    SymbolsJson = JsonSerializer.Serialize(config.Symbols),
                    IntervalsJson = JsonSerializer.Serialize(config.Intervals),
                    config.StartDate,
                    config.EndDate,
                    config.InitialCapital,
                    StartTime = DateTime.UtcNow
                });
                _logger.LogInformation("Initialized backtest run {RunId} in database.", runId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize backtest run {RunId} in database.", runId);
                throw;
            }
        }

        public async Task UpdateBacktestRunStatusAsync(Guid runId, string status, DateTime timestamp, string? errorMessage = null)
        {
            const string sql = @"
                UPDATE BacktestRuns
                SET Status = @Status, ErrorMessage = @ErrorMessage, EndTime = CASE WHEN @Status IN ('Completed', 'Failed') THEN @Timestamp ELSE EndTime END
                WHERE RunId = @RunId;";
            try
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(sql, new { RunId = runId, Status = status, ErrorMessage = errorMessage, Timestamp = timestamp });
                _logger.LogDebug("Updated status for backtest run {RunId} to {Status}.", runId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for backtest run {RunId} to {Status}.", runId, status);
            }
        }

        public async Task SaveBacktestTradesAsync(Guid runId, IEnumerable<TradeSummary> trades)
        {
            // Assumes table BacktestTrades exists
            // Using a simple loop with ExecuteAsync - for large volumes, consider table-valued parameters or bulk copy libraries
            const string sql = @"
                INSERT INTO Constellations.BacktestTrades (RunId, Symbol, Interval, StrategyName, EntryDate, ExitDate, EntryPrice, ExitPrice, Quantity, Direction, ProfitLoss)
                VALUES (@RunId, @Symbol, @Interval, @StrategyName, @EntryDate, @ExitDate, @EntryPrice, @ExitPrice, @Quantity, @Direction, @ProfitLoss);";
            try
            {
                using var connection = CreateConnection();
                int affectedRows = await connection.ExecuteAsync(sql, trades.Select(t => new
                {
                    // Ensure mapping matches table columns precisely
                    RunId = runId,
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
                throw; // Fail the operation if trades can't be saved
            }
        }

        public async Task UpdateBacktestRunTotalsAsync(Guid runId, BacktestResult result)
        {
            if (result.Trades.Any())
            {
                // Call the dedicated save method within the repository
                await SaveBacktestTradesAsync(runId, result.Trades);
            }

            // Assumes table BacktestRuns exists and has columns for totals/metrics
            const string sql = @"
                UPDATE BacktestRuns
                SET Status = @Status,
                    EndTime = @EndTime,
                    TotalProfitLoss = @TotalProfitLoss,
                    TotalReturn = @TotalReturn,
                    MaxDrawdown = @MaxDrawdown,
                    WinRate = @WinRate,
                    TotalTrades = @TotalTrades,
                    ErrorMessage = @ErrorMessage -- Update error message on finalization too
                WHERE RunId = @RunId;";
            try
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(sql, new
                {
                    RunId = runId,
                    result.Status, // Should be 'Completed' or 'Failed' here
                    EndTime = result.EndTime ?? DateTime.UtcNow, // Use result EndTime
                    result.TotalProfitLoss,
                    result.TotalReturn,
                    result.MaxDrawdown,
                    result.WinRate,
                    result.TotalTrades,
                    result.ErrorMessage
                });
                _logger.LogInformation("Updated final totals for backtest run {RunId} with status {Status}.", runId, result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update final totals for backtest run {RunId}.", runId);
                // Log is likely sufficient here, as the run is already finished/failed.
            }
        }

        public async Task<BacktestRun?> GetBacktestRunAsync(Guid runId)
        {
            // Fetches the summary data for a run
            const string sql = "SELECT * FROM BacktestRuns WHERE RunId = @RunId;";
            try
            {
                using var connection = CreateConnection();
                // Use the BacktestRun model defined earlier for mapping
                return await connection.QuerySingleOrDefaultAsync<BacktestRun>(sql, new { RunId = runId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get backtest run summary for {RunId}.", runId);
                return null;
            }
        }

        public async Task<IEnumerable<TradeSummary>> GetBacktestTradesAsync(Guid runId)
        {
            // Fetches all trades associated with a run
            const string sql = "SELECT * FROM BacktestTrades WHERE RunId = @RunId ORDER BY EntryDate;";
            try
            {
                using var connection = CreateConnection();
                // Assumes BacktestTrades columns map directly to TradeSummary properties
                return await connection.QueryAsync<TradeSummary>(sql, new { RunId = runId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get backtest trades for {RunId}.", runId);
                return Enumerable.Empty<TradeSummary>(); // Return empty list on error
            }
        }
    }
}