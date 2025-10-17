using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Orchestration.Implementations
{
    public class PortfolioBacktestRunner : IPortfolioBacktestRunner
    {
        private readonly IBacktestRunner _backtestRunner;
        private readonly IWalkForwardRepository _walkForwardRepoitory;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ITradeService _tradeService;
        private readonly ILogger<PortfolioBacktestRunner> _logger;

        public PortfolioBacktestRunner(
            IBacktestRunner coreBacktestRunner,
            IWalkForwardRepository walkForwardRepo,
            IBackgroundJobClient hangfireClient,
            ITradeService tradeService,
            ILogger<PortfolioBacktestRunner> logger)
        {
            _backtestRunner = coreBacktestRunner;
            _walkForwardRepoitory = walkForwardRepo;
            _backgroundJobClient = hangfireClient;
            _tradeService = tradeService;
            _logger = logger;
        }

        public async Task ExecuteBacktest(Guid sessionId, DateTime oosStartDate, DateTime oosEndDate)
        {
            var session = await _walkForwardRepoitory.GetSessionAsync(sessionId);
            if (oosStartDate >= session.EndDate)
            {
                _logger.LogInformation("Walk-forward session {SessionId} complete. Final backtest date {Date} exceeds session end date.", sessionId, oosStartDate);
                await _walkForwardRepoitory.UpdateSessionStatusAsync(sessionId, "Completed");
                return;
            }

            _logger.LogInformation("MAIN LOOP: Running 1-Month OOS Backtest for {Date:yyyy-MM} on SessionId: {SessionId}",
                oosStartDate, sessionId);

            var activeSleeve = await _walkForwardRepoitory.GetActiveSleeveAsync(sessionId, oosStartDate);
            _logger.LogInformation($"Active sleeve count: {activeSleeve.Count()}");

            var backtestConfig = new BacktestConfiguration
            {
                RunId = Guid.NewGuid(),
                SessionId = session.SessionId,
                StrategyName = session.StrategyName,
                Symbols = activeSleeve.Select(s => s.Symbol).ToList(),
                Intervals = new List<string> { "60min" },
                PortfolioParameters = activeSleeve.ToDictionary(
                    s => s.Symbol,
                    s => JsonSerializer.Deserialize<Dictionary<string, object>>(s.OptimizedParametersJson)
                ),
                StartDate = oosStartDate,
                EndDate = oosEndDate,
                InitialCapital = session.CurrentCapital
            };

            await _tradeService.InitializeBacktestRunAsync(backtestConfig, backtestConfig.RunId);

            var result = await _backtestRunner.RunPortfolioBacktest(backtestConfig, backtestConfig.RunId);

            if (result == null)
            {
                _logger.LogError("Backtest result is null for SessionId {SessionId} during period starting {StartDate}. Aborting further processing.", sessionId, oosStartDate);
                return;
            }

            await _tradeService.UpdateBacktestPerformanceMetrics(backtestConfig.RunId, result, backtestConfig.InitialCapital);
            await _tradeService.UpdateBacktestRunStatusAsync(backtestConfig.RunId, "Completed");
            var finalCapital = backtestConfig.InitialCapital + result.TotalProfitLoss; 
            await _walkForwardRepoitory.UpdateSessionCapitalAsync(sessionId, finalCapital);

            //if (oosStartDate.Month == 12)
            //{
            //    _backgroundJobClient.Enqueue<ISleeveSelectionOrchestrator>(orchestrator =>
            //        orchestrator.ReselectAnnualSleeve(cycleTrackerId, sessionId, oosEndDate));
            //    _logger.LogInformation("End of year reached. Enqueued annual re-selection job.");
            //}
            //else
            //{
            //    _backgroundJobClient.Enqueue<IFineTuneOrchestrator>(orchestrator =>
            //        orchestrator.ExecuteFineTune(sessionId, oosEndDate));
            //    _logger.LogInformation("Monthly backtest complete. Enqueued fine-tuning job.");
            //}
        }
    }
}