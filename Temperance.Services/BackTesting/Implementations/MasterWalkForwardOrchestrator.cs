using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class MasterWalkForwardOrchestrator : IMasterWalkForwardOrchestrator
    {
        private readonly ILogger<MasterWalkForwardOrchestrator> _logger;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IConductorClient _conductorClient;
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly IPerformanceRepository _performanceRepository;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public object OptimizationMode { get; private set; }

        public MasterWalkForwardOrchestrator(
            IBackgroundJobClient backgroundJobClient,
            ILogger<MasterWalkForwardOrchestrator> logger,
            ISecuritiesOverviewService securitiesOverviewService,
            IConductorService conductorService,
            ITradeService tradeService,
            IConductorClient conductorClient,
            IWalkForwardRepository walkForwardRepository,
            IPerformanceRepository performanceRepository)
        {
            _logger = logger;
            _securitiesOverviewService = securitiesOverviewService;
            _conductorClient = conductorClient;
            _walkForwardRepository = walkForwardRepository;
            _performanceRepository = performanceRepository;
            _backgroundJobClient = backgroundJobClient;
        }

        public async Task ExecuteCycle(Guid sessionId, DateTime cycleStartDate)
        {
            _logger.LogInformation("PHASE 1: Starting Initial Bulk Training for SessionId: {SessionId}", sessionId);

            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogError("Could not find session {SessionId}. Aborting.", sessionId);
                await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Failed");
                return;
            }
            if (cycleStartDate >= session.EndDate)
            {
                _logger.LogInformation("ORCHESTRATOR: Walk-forward session {SessionId} complete.", sessionId);
                await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Completed");
                return;
            }

            var inSampleStartDate = cycleStartDate.AddYears(-session.OptimizationWindowYears);
            var inSampleEndDate = cycleStartDate.AddDays(-1);

            List<string> fullUniverse;
            BacktestRun previousRun = await _walkForwardRepository.GetLatestRunForSessionAsync(sessionId);

            int activeSleeveSize = 50;
            var optimizationMode = Data.Models.Backtest.OptimizationMode.Train;

            if (previousRun == null)
            {
                _logger.LogInformation($"First cycle: getting initial universe from securities...");
                fullUniverse = await _securitiesOverviewService.GetSecurities();
            }
            else
            {
                _logger.LogInformation($"Subsequent cycle: Ranking securities based on performance from RunId");
                var activeSleevePerformance = await _performanceRepository.GetSleeveComponentsAsync(previousRun.RunId);
                var shadowSleevePerformance = await _performanceRepository.GetShadowPerformanceAsync(previousRun.RunId);

                var combinedPerformance = activeSleevePerformance
                    .Select(p => new { p.Symbol, p.SharpeRatio })
                    .Concat(shadowSleevePerformance.Select(p => new { p.Symbol, p.SharpeRatio }));

                fullUniverse = combinedPerformance
                    .OrderByDescending(p => p.SharpeRatio)
                    .Select(p => p.Symbol)
                    .ToList();

                activeSleeveSize = Math.Max(activeSleeveSize, activeSleevePerformance.Count());
                optimizationMode = Data.Models.Backtest.OptimizationMode.FineTune;
            }

            if (!fullUniverse.Any())
            {
                _logger.LogError("No securities found for the initial universe. Aborting.");
                return;
            }

            var activeUniverse = fullUniverse.Take(activeSleeveSize).ToList();
            var shadowUniverse = fullUniverse.Skip(activeSleeveSize).ToList();

            var tradingPeriodEndDate = cycleStartDate.AddYears(session.TradingWindowYears).AddDays(-1);
            if(tradingPeriodEndDate > session.EndDate)
                tradingPeriodEndDate = session.EndDate;

            var activeRunId = Guid.NewGuid();
            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(
                orchestrator => orchestrator.ExecuteNextPeriod(sessionId, cycleStartDate, tradingPeriodEndDate));

            var shadowRunId = Guid.NewGuid();
            _backgroundJobClient.Enqueue<IShadowBacktestOrchestrator>(
                orchestrator => orchestrator.Execute(sessionId, shadowRunId, shadowUniverse, cycleStartDate, tradingPeriodEndDate));

            _logger.LogInformation("Found {SymbolCount} securities for initial training.", fullUniverse.Count);

            var batchRequest = new OptimizationBatchRequest
            {
                SessionId = sessionId,
                StrategyName = session.StrategyName,
                Mode = optimizationMode,
                InSampleStartDate = inSampleStartDate,
                InSampleEndDate = inSampleEndDate,
                Symbols = activeUniverse,
                Interval = "60min",
            };

            await _conductorClient.DispatchOptimizationBatchAsync(batchRequest);

            _logger.LogInformation("PHASE 1: Batch dispatch request sent to Conductor for {SymbolCount} securities. This job is now complete.", batchRequest.Symbols.Count);
        }
    }
}
