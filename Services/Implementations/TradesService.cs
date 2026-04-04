using System.Text.Json;
using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Strategy;
using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.Repositories.Interfaces;
using Temperance.Constellations.Repositories.Interfaces.Trade.Interfaces;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Backtesting;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Models.Trading;

namespace Temperance.Constellations.Services.Implementations
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

        public async Task<TradeSummary?> GetActiveTradeForPositionAsync(Guid positionId)
        {
            return await _tradeRepository.GetActiveTradeByPositionIdAsync(positionId);
        }

        public async Task<int> ExecuteOrderAsync(Order order)
        {
            return await _tradeRepository.ExecuteOrderAsync(order);
        }

        public async Task<int> LogStrategyAsync(StrategyLog log)
        {
            return await _tradeRepository.LogStrategyAsync(log);
        }

        public async Task<int> SaveTradeAsync(Trade trade)
        {
            return await _tradeRepository.SaveTradeAsync(trade);
        }

        public async Task<int> UpdatePositionAsync(Models.Trading.Position position)
        {
            return await _tradeRepository.UpdatePositionAsync(position);
        }

        public async Task CheckTradeExitsAsync()
        {
            await _tradeRepository.CheckTradeExitsAsync();
        }

        public async Task InitializeBacktestRunAsync(BacktestConfiguration config, Guid runId)
        {
            await _tradeRepository.InitializeBacktestRunAsync(runId, config);
        }

        public async Task<(decimal Cash, List<Models.Trading.Position> OpenPositions)?> GetLatestPortfolioStateAsync(Guid sessionId)
        {
            return await _tradeRepository.GetLatestPortfolioStateAsync(sessionId);
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
            await _tradeRepository.UpdateBacktestRunStatusAsync(runId, status, DateTime.UtcNow, errorMessage);
        }

        public async Task UpdateBacktestPerformanceMetrics(Guid runId, BacktestResult metrics, decimal initialCapital)
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

        public Task FinalizeBacktestRunAsync(Guid runId, BacktestResult result)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<WalkForwardSleeve>> GetSleevesForSessionAsync(Guid sessionId, DateTime tradingPeriodStartDate)
        {
            return await _tradeRepository.GetSleevesForSessionAsync(sessionId, tradingPeriodStartDate);
        }

        public async Task<WalkForwardSessionModel?> GetSessionAsync(Guid sessionId)
        {
            return await _tradeRepository.GetSessionAsync(sessionId);
        }

        public async Task UpdateSessionCapitalAsync(Guid sessionId, decimal newCapital)
        {
            await _tradeRepository.UpdateSessionCapitalAsync(sessionId, newCapital);
        }

        public async Task<IEnumerable<BacktestRunModel>> GetBacktestRunsForSessionAsync(Guid sessionId, DateTime startDate, DateTime endDate)
        {
            return await _tradeRepository.GetBacktestRunsForSessionAsync(sessionId, startDate, endDate);
        }

        public async Task SaveSleevesAsync(IEnumerable<WalkForwardSleeve> sleeves)
        {
            await _tradeRepository.SaveSleevesAsync(sleeves);
        }

        public async Task SaveTradesBulkAsync(IEnumerable<TradeSummary> trades)
        {
            await _tradeRepository.SaveTradesBulkAsync(trades);
        }

        public async Task<IEnumerable<TradeSummary>> GetTradesByRunIdAsync(Guid lastActiveRunId)
        {
            return await _tradeRepository.GetTradesByRunIdAsync(lastActiveRunId);
        }
    }
}