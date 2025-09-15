using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces; // Your existing IBacktestRunner

namespace Temperance.Services.BackTesting.Orchestration.Implementations
{
    public class PortfolioBacktestOrchestrator : IPortfolioBacktestOrchestrator
    {
        private readonly IBacktestRunner _backtestEngine;
        private readonly IWalkForwardRepository _walkForwardRepo;
        private readonly IBackgroundJobClient _hangfireClient;
        private readonly ILogger<PortfolioBacktestOrchestrator> _logger;

        public PortfolioBacktestOrchestrator(
            IBacktestRunner backtestEngine,
            IWalkForwardRepository walkForwardRepo,
            IBackgroundJobClient hangfireClient,
            ILogger<PortfolioBacktestOrchestrator> logger)
        {
            _backtestEngine = backtestEngine;
            _walkForwardRepo = walkForwardRepo;
            _hangfireClient = hangfireClient;
            _logger = logger;
        }

        public async Task ExecuteNextPeriod(Guid sessionId, DateTime oosStartDate)
        {
            // --- Step 1: Manage State ---
            // The first job of the orchestrator is to check the overall state of the 20-year session.
            var session = await _walkForwardRepo.GetSessionAsync(sessionId);
            if (oosStartDate >= session.EndDate)
            {
                _logger.LogInformation("ORCHESTRATOR: Walk-forward session {SessionId} complete.", sessionId);
                await _walkForwardRepo.UpdateSessionStatusAsync(sessionId, "Completed");
                return;
            }

            var oosEndDate = oosStartDate.AddMonths(1).AddDays(-1);
            _logger.LogInformation("ORCHESTRATOR: Preparing 1-Month OOS Backtest for {Date:yyyy-MM}", oosStartDate);

            // --- Step 2: Prepare Inputs for the Engine ---
            // The orchestrator gathers all the necessary data for the next run.
            var activeSleeve = (await _walkForwardRepo.GetActiveSleeveAsync(sessionId, oosStartDate)).ToList();
            if (!activeSleeve.Any())
            {
                _logger.LogWarning("ORCHESTRATOR: No active sleeve for {Date}. Skipping backtest.", oosStartDate);
            }
            else
            {
                // It assembles the specific instructions for this one-month mission.
                var config = new BacktestConfiguration
                {
                    RunId = Guid.NewGuid(),
                    SessionId = session.SessionId,
                    StartDate = oosStartDate,
                    EndDate = oosEndDate,
                    InitialCapital = session.CurrentCapital,
                    Symbols = activeSleeve.Select(s => s.Symbol).ToList(),
                    StrategyName = session.StrategyName
                };

                // --- Step 3: Delegate to the Engine ---
                // The orchestrator calls your existing, powerful IBacktestRunner.RunBacktest method.
                // It does NOT contain any of the backtesting logic itself.
                await _backtestEngine.RunBacktest(config, config.RunId);
            }

            // --- Step 4: Chain the Next Job ---
            // The orchestrator's final and most important job is to advance the state machine.
            // It decides what happens next in the 20-year sequence.
            if (oosStartDate.Month == 12)
            {
                // If it's December, the next step is the annual re-selection.
                _hangfireClient.Enqueue<ISleeveSelectionOrchestrator>(o => o.ReselectAnnualSleeve(sessionId, oosEndDate));
                _logger.LogInformation("ORCHESTRATOR: Enqueued annual re-selection job.");
            }
            else
            {
                // Otherwise, the next step is to fine-tune the models.
                _hangfireClient.Enqueue<IFineTuneOrchestrator>(o => o.ExecuteFineTune(sessionId, oosEndDate));
                _logger.LogInformation("ORCHESTRATOR: Enqueued fine-tuning job.");
            }
        }
    }
}