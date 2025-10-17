using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;

namespace Temperance.Services.BackTesting.Orchestration.Implementations
{
    public class PortfolioBacktestOrchestrator : IPortfolioBacktestOrchestrator
    {
        private readonly IBacktestRunner _backtestEngine;
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<PortfolioBacktestOrchestrator> _logger;

        public PortfolioBacktestOrchestrator(
            IBacktestRunner backtestEngine,
            IWalkForwardRepository walkForwardRepo,
            IBackgroundJobClient hangfireClient,
            ILogger<PortfolioBacktestOrchestrator> logger)
        {
            _backtestEngine = backtestEngine;
            _walkForwardRepository = walkForwardRepo;
            _backgroundJobClient = hangfireClient;
            _logger = logger;
        }

        public async Task ExecuteNextPeriod(Guid cycleTrackerId, Guid sessionId, DateTime oosStartDate, DateTime oosEndDate)
        {
            try
            {
                _logger.LogInformation($"THIS IS THE OOSSTARTDATE: {oosStartDate} inside ExecuteNextPeriod.");

                var session = await _walkForwardRepository.GetSessionAsync(sessionId);
                if (oosStartDate >= session.EndDate)
                {
                    _logger.LogInformation("ORCHESTRATOR: Walk-forward session {SessionId} complete.", sessionId);
                    await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Completed");
                    return;
                }

                _logger.LogInformation("ORCHESTRATOR: Preparing 1-Month OOS Backtest for {Date:yyyy-MM}", oosStartDate);

                var activeSleeve = (await _walkForwardRepository.GetActiveSleeveAsync(sessionId, oosStartDate)).ToList();
                var activeSleeveSymbols = activeSleeve.Select(s => s.Symbol).ToList();

                if (!activeSleeve.Any())
                {
                    _logger.LogWarning("ORCHESTRATOR: No active sleeve for {Date}. Skipping backtest.", oosStartDate);
                }
                else
                {
                    var sleeveParameters = await _walkForwardRepository.GetLatestParametersForSleeveAsync(sessionId, activeSleeveSymbols);

                    var config = new BacktestConfiguration
                    {
                        RunId = Guid.NewGuid(),
                        SessionId = session.SessionId,
                        StartDate = oosStartDate,
                        EndDate = oosEndDate,
                        InitialCapital = session.CurrentCapital,
                        Symbols = activeSleeve.Select(s => s.Symbol).ToList(),
                        StrategyName = session.StrategyName,
                        PortfolioParameters = sleeveParameters,
                        MaxParallelism = 16,
                    };

                    await _backtestEngine.RunPortfolioBacktest(config, config.RunId);
                }

                if (oosStartDate.Month == 12)
                {
                    _backgroundJobClient.Enqueue<ISleeveSelectionOrchestrator>(o => o.ReselectAnnualSleeve(cycleTrackerId, sessionId, oosEndDate));
                    _logger.LogInformation("ORCHESTRATOR: Enqueued annual re-selection job.");
                }
                else
                {
                    _backgroundJobClient.Enqueue<IFineTuneOrchestrator>(o => o.ExecuteFineTune(sessionId, oosEndDate));
                    _logger.LogInformation("ORCHESTRATOR: Enqueued fine-tuning job.");
                }
            }
            finally
            {
                _backgroundJobClient.Enqueue<IMasterWalkForwardOrchestrator>(
                    master => master.SignalBacktestCompletion(cycleTrackerId, BacktestType.Portfolio));
            }
        }
    }
}