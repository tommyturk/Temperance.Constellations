using Microsoft.Extensions.Logging; // Inject ILogger
using System.Text.Json; // For deserializing config if needed
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;
using TradingApp.src.Core.Services.Interfaces;

namespace TradingApp.src.Core.Services.Implementations
{
    public class TradesService : ITradeService
    {
        private readonly ITradeRepository _tradeRepository;
        private readonly IBacktestRepository _backtestRepository; 
        private readonly ILogger<TradesService> _logger;

        public TradesService(ITradeRepository tradeRepository, IBacktestRepository backtestRepository, ILogger<TradesService> logger)
        {
            _tradeRepository = tradeRepository;
            _backtestRepository = backtestRepository;
            _logger = logger;
        }

        public async Task<int> ExecuteOrderAsync(Order order)
        {
            return await _tradeRepository.ExecuteOrderAsync(order);
        }

        public Task<int> LogStrategyAsync(StrategyLog log)
        {
            return _tradeRepository.LogStrategyAsync(log);
        }

        public async Task<int> SaveTradeAsync(Trade trade)
        {
            return await _tradeRepository.SaveTradeAsync(trade);
        }

        public async Task<int> UpdatePositionAsync(Position position)
        {
            return await _tradeRepository.UpdatePositionAsync(position);
        }

        public async Task CheckTradeExitsAsync()
        {
            await _tradeRepository.CheckTradeExitsAsync();
        }

        public async Task InitializeBacktestRunAsync(BacktestConfiguration config, Guid runId)
        {
            _logger.LogInformation("Service request to initialize backtest run {RunId}", runId);
            await _tradeRepository.InitializeBacktestRunAsync(runId, config);
        }

        public Task<(double Cash, List<Position> OpenPositions)?> GetLatestPortfolioStateAsync(Guid sessionId)
        {
            return _tradeRepository.GetLatestPortfolioStateAsync(sessionId);
        }

        public async Task InitializePairBacktestRunAsync(PairsBacktestConfiguration config, Guid runId)
        {
            string strategyParametersJson = JsonSerializer.Serialize(config.StrategyParameters);

            Dictionary<string, object> strategyParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(strategyParametersJson) 
                ?? new Dictionary<string, object>();

            BacktestConfiguration backtestConfig = new BacktestConfiguration
            {
                StrategyName = config.StrategyName,
                StrategyParameters = strategyParameters,
                Symbols = config.PairsToTest.Select(x => $"{x.SymbolA},{x.SymbolB}").ToList(),
                Intervals = new List<string>() { config.Interval },
                StartDate = config.StartDate,
                EndDate = config.EndDate,
                InitialCapital = config.InitialCapital
            };
            await InitializeBacktestRunAsync(backtestConfig, runId);
        }

        public async Task UpdateBacktestRunStatusAsync(Guid runId, string status, string? errorMessage = null)
        {
            _logger.LogInformation($"Service request to update status for backtest run {runId} to {status}");
            await _tradeRepository.UpdateBacktestRunStatusAsync(runId, status, DateTime.UtcNow, errorMessage);
        }

        public async Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult metrics, double initialCapital)
        {
            _logger.LogInformation("Service request to update performance metrics for backtest run {RunId}", runId);
            await _backtestRepository.UpdateBacktestPerformanceMetrics(runId, metrics, initialCapital);
        }

        public async Task SaveTradesAsync(Guid runId, IEnumerable<TradeSummary> trades)
        {
            if (!trades.Any()) return; 
            _logger.LogInformation("Service request to save {TradeCount} trades for backtest run {RunId}", trades.Count(), runId);
            await _tradeRepository.SaveBacktestTradesAsync(runId, trades);
        }

        public async Task SaveBacktestResults(Guid runId, IEnumerable<TradeSummary> trades)
        {
            if (trades != null && trades.Any())
            {
                await _backtestRepository.SaveBacktestTradesAsync(runId, trades);
            }
        }

        public async Task SaveBacktestResults(Guid runId, BacktestResult backtestResult, string symbol, string interval)
        {
            if (backtestResult?.Trades != null && backtestResult.Trades.Any())
            {
                foreach (var trade in backtestResult.Trades)
                {
                    trade.Symbol = symbol;
                    trade.Interval = interval;
                }
                await _tradeRepository.SaveBacktestTradesAsync(runId, backtestResult.Trades);
            }
        }
        public async Task SaveOrUpdateBacktestTrade(TradeSummary trade)
        {
            await _tradeRepository.SaveOrUpdateBacktestTradeAsync(trade);
        }

        public async Task UpdateBacktestRunTotalsAsync(Guid runId, BacktestResult result)
        {
            _logger.LogInformation("Service request to update final totals for backtest run {RunId}", runId);
            await _tradeRepository.UpdateBacktestRunTotalsAsync(runId, result);
        }

        public async Task<string?> GetBacktestRunStatusAsync(Guid runId)
        {
            _logger.LogDebug("Service request to get status for backtest run {RunId}", runId);
            var run = await _tradeRepository.GetBacktestRunAsync(runId);
            return run?.Status; // Return only the status string
        }

        public async Task<BacktestResult?> GetBacktestResultAsync(Guid runId)
        {
            _logger.LogInformation("Service request to get results for backtest run {RunId}", runId);
            var runData = await _tradeRepository.GetBacktestRunAsync(runId);
            if (runData == null) return null;
            var trades = await _tradeRepository.GetBacktestTradesAsync(runId);
            // Construct the BacktestResult object
            var result = new BacktestResult
            {
                RunId = runData.RunId,
                StartTime = runData.StartTime,
                EndTime = runData.EndTime,
                Status = runData.Status,
                TotalTrades = runData.TotalTrades ?? trades.Count(), // Use count if TotalTrades wasn't updated yet
                TotalProfitLoss = runData.TotalProfitLoss,
                TotalReturn = runData.TotalReturn,
                MaxDrawdown = runData.MaxDrawdown,
                WinRate = runData.WinRate,
                ErrorMessage = runData.ErrorMessage,
                Trades = trades.ToList(), // Assign fetched trades
                Configuration = new BacktestConfiguration // Reconstruct config
                {
                    StrategyName = runData.StrategyName,
                    StrategyParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(runData.ParametersJson ?? "{}")!,
                    Symbols = JsonSerializer.Deserialize<List<string>>(runData.SymbolsJson ?? "[]")!,
                    Intervals = JsonSerializer.Deserialize<List<string>>(runData.IntervalsJson ?? "[]")!,
                    StartDate = runData.StartDate,
                    EndDate = runData.EndDate,
                    InitialCapital = runData.InitialCapital
                }
                // Equity curve not stored/retrieved here - would need calculation or separate storage
            };
            // If Status is Completed but totals are null, maybe recalculate? Optional.
            // if(result.Status == "Completed" && result.TotalReturn == null && result.Trades.Any()) {
            //     _logger.LogWarning("Recalculating metrics for completed run {RunId} as totals were null.", runId);
            //     // Inject IPerformanceCalculator here if needed for recalculation
            //     // _performanceCalculator.CalculatePerformanceMetrics(result, result.Configuration.InitialCapital);
            // }

            return result;
        }

        public Task FinalizeBacktestRunAsync(Guid runId, BacktestResult result)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<WalkForwardSleeve>> GetSleevesForSessionAsync(Guid sessionId, DateTime tradingPeriodStartDate)
        {
            return _tradeRepository.GetSleevesForSessionAsync(sessionId, tradingPeriodStartDate);
        }

        public Task<WalkForwardSession?> GetSessionAsync(Guid sessionId)
        {
            return _tradeRepository.GetSessionAsync(sessionId);
        }

        public Task UpdateSessionCapitalAsync(Guid sessionId, double newCapital)
        {
            return _tradeRepository.UpdateSessionCapitalAsync(sessionId, newCapital);
        }

        public Task<IEnumerable<BacktestRun>> GetBacktestRunsForSessionAsync(Guid sessionId, DateTime startDate, DateTime endDate)
        {
            return _tradeRepository.GetBacktestRunsForSessionAsync(sessionId, startDate, endDate);
        }

        public Task SaveSleevesAsync(IEnumerable<WalkForwardSleeve> sleeves)
        {
            return _tradeRepository.SaveSleevesAsync(sleeves);
        }
    }
}