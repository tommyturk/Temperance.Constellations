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
        private readonly IOptimizationRepository _optimizationRepository;
        private const int OosCycleMonths = 6;
        public object OptimizationMode { get; private set; }

        public MasterWalkForwardOrchestrator(
            IBackgroundJobClient backgroundJobClient,
            ILogger<MasterWalkForwardOrchestrator> logger,
            ISecuritiesOverviewService securitiesOverviewService,
            IConductorService conductorService,
            ITradeService tradeService,
            IConductorClient conductorClient,
            IWalkForwardRepository walkForwardRepository,
            IPerformanceRepository performanceRepository,
            IOptimizationRepository optimizationRepository)
        {
            _logger = logger;
            _securitiesOverviewService = securitiesOverviewService;
            _conductorClient = conductorClient;
            _walkForwardRepository = walkForwardRepository;
            _performanceRepository = performanceRepository;
            _backgroundJobClient = backgroundJobClient;
            _optimizationRepository = optimizationRepository;
        }

        public async Task InitiateCycle(Guid sessionId, DateTime inSampleEndDate)
        {
            _logger.LogInformation("PHASE 2: Starting Out-of-Sample Backtest for SessionId: {SessionId}", sessionId);

            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogError("Could not find session {SessionId}. Aborting.", sessionId);
                await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Failed");
                return;
            }

            var oosStartDate = inSampleEndDate.AddDays(1);

            if (oosStartDate > session.EndDate)
            {
                _logger.LogInformation("ORCHESTRATOR: Walk-forward session {SessionId} complete. No further OOS periods to run.", sessionId);
                await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Completed");
                return;
            }

            var oosEndDate = oosStartDate.AddMonths(OosCycleMonths).AddDays(-1);
            if (oosEndDate > session.EndDate)
                oosEndDate = session.EndDate;

            var inSampleStartDate = inSampleEndDate.AddYears(-session.OptimizationWindowYears).AddDays(1);
            var strategyName = session.StrategyName;
            var interval = "60min";

            _logger.LogInformation("Fetching optimization results for {Strategy} on {Interval} from {IS_Start} to {IS_End}",
                strategyName, interval, inSampleStartDate, inSampleEndDate);

            var optimizationResults = await _optimizationRepository.GetOptimizationResultsByWindowAsync(
                    strategyName,
                    interval,
                    inSampleStartDate,
                    inSampleEndDate
                );

            if (optimizationResults == null || !optimizationResults.Any())
            {
                _logger.LogError("No optimization results found for session {SessionId} with the specified window. Aborting.", sessionId);
                await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Failed");
                return;
            }

            var fullUniverse = optimizationResults
                .OrderByDescending(x => x.Metrics.SharpeRatio)
                .Select(r => r.Symbol)
                .ToList();

            int activeSleeveSize = 50; 
            var activeUniverse = fullUniverse.Take(activeSleeveSize).ToList();
            var shadowUniverse = fullUniverse.Skip(activeSleeveSize).ToList();
            // please print the top 10 securities symbol and sharpe ratio into logger
            _logger.LogInformation("Top 10 Optimization Results (Symbol : SharpeRatio): {Results}",
                string.Join(", ", optimizationResults
                    .OrderByDescending(x => x.Metrics.SharpeRatio)
                    .Take(10)
                    .Select(r => $"{r.Symbol} : {r.Metrics.SharpeRatio:F2}")));
            _logger.LogInformation("Active Sleeve (Top {Count}): {Symbols}", activeUniverse.Count, string.Join(",", activeUniverse.Take(5)) + "...");
            _logger.LogInformation("Shadow Sleeve ({Count})", shadowUniverse.Count);

            var cycleTracker = new CycleTracker
            {
                CycleTrackerId = Guid.NewGuid(),
                SessionId = sessionId,

                CycleStartDate = inSampleEndDate,
                OosStartDate = oosStartDate,
                OosEndDate = oosEndDate,

                PortfolioBacktestRunId = Guid.NewGuid(), 
                ShadowBacktestRunId = Guid.NewGuid(),    

                IsPortfolioBacktestComplete = false,
                IsShadowBacktestComplete = false,
                IsOptimizationDispatched = false,
                CreatedAt = DateTime.UtcNow
            };

            await _walkForwardRepository.CreateCycleTracker(cycleTracker);

            _logger.LogInformation("Enqueuing Active and Shadow backtests for CycleTrackerId {CycleTrackerId}.", cycleTracker.CycleTrackerId);

            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(
                orchestrator => orchestrator.ExecuteNextPeriod(
                    cycleTracker.CycleTrackerId,
                    sessionId,
                    cycleTracker.PortfolioBacktestRunId, 
                    activeUniverse,                 
                    inSampleStartDate,
                    inSampleEndDate,
                    oosStartDate,
                    oosEndDate
                ));

            _backgroundJobClient.Enqueue<IShadowBacktestOrchestrator>(
                orchestrator => orchestrator.Execute(
                    cycleTracker.CycleTrackerId,
                    sessionId,
                    cycleTracker.ShadowBacktestRunId,    
                    shadowUniverse,                      
                    oosStartDate,
                    oosEndDate
                ));

            _logger.LogInformation(
                "PHASE 2: Enqueued Active and Shadow backtests for CycleTrackerId {CycleTrackerId}. Waiting for completion signals.",
                cycleTracker.CycleTrackerId);
        }

        [AutomaticRetry(Attempts = 3)]
        public async Task SignalBacktestCompletion(Guid cycleTrackerId, BacktestType backtestType)
        {
            _logger.LogInformation("Received completion signal for {BacktestType} backtest in CycleTrackerId: {CycleTrackerId}", backtestType, cycleTrackerId);

            var tracker = await _walkForwardRepository.SignalCompletionAndCheckIfReady(cycleTrackerId, backtestType);

            if (tracker != null && tracker.IsPortfolioBacktestComplete && tracker.IsShadowBacktestComplete && !tracker.IsOptimizationDispatched)
                _backgroundJobClient.Enqueue(() => DispatchOptimizationPhase(cycleTrackerId));
            else if (tracker == null)
                _logger.LogError("CycleTrackerId {CycleTrackerId} not found while signaling backtest completion.", cycleTrackerId);
            else
                _logger.LogInformation("Waiting for the other backtest to complete for CycleTrackerId: {CycleTrackerId}.", cycleTrackerId);
        }

        public async Task DispatchOptimizationPhase(Guid cycleTrackerId)
        {
            var tracker = await _walkForwardRepository.GetCycleTrackerAsync(cycleTrackerId);
            if (tracker == null)
            {
                _logger.LogError("CycleTrackerId {CycleTrackerId} not found. Cannot dispatch optimization phase.", cycleTrackerId);
                return;
            }
            var session = await _walkForwardRepository.GetSessionAsync(tracker.SessionId);
            if (session == null)
            {
                _logger.LogError("SessionId {SessionId} not found for CycleTrackerId {CycleTrackerId}. Cannot dispatch optimization phase.", tracker.SessionId, cycleTrackerId);
                return;
            }

            var previousRun = await _walkForwardRepository.GetLatestRunForSessionAsync(tracker.SessionId);
            List<string> fullUniverse;
            var optimizationMode = Data.Models.Backtest.OptimizationMode.Train;
            int activeSleeveSize = 50;

            if (previousRun == null)
                fullUniverse = await _securitiesOverviewService.GetSecurities();
            else
            {
                var activeSleevePerformance = await _performanceRepository.GetSleeveComponentsAsync(previousRun.RunId);
                var shadowSleevePerformance = await _performanceRepository.GetShadowPerformanceAsync(previousRun.RunId);

                var projectedActive = activeSleevePerformance
                    .Select(p => new { p.Symbol, p.SharpeRatio });
                var projectedShadow = shadowSleevePerformance
                    .Select(p => new { p.Symbol, p.SharpeRatio });

                var combinedPerformance = projectedActive
                    .Concat(projectedShadow);

                fullUniverse = combinedPerformance
                    .OrderByDescending(p => p.SharpeRatio)
                    .Select(p => p.Symbol)
                    .Distinct()
                    .ToList();

                activeSleeveSize = Math.Max(activeSleeveSize, activeSleevePerformance.Count());
                optimizationMode = Data.Models.Backtest.OptimizationMode.FineTune;
            }
            var activeUniverse = fullUniverse.Take(activeSleeveSize).ToList();

            var inSampleStartDate = tracker.CycleStartDate.AddYears(-session.OptimizationWindowYears);
            var inSampleEndDate = tracker.CycleStartDate.AddDays(-1);
            _logger.LogInformation("PHASE 2: Dispatching Optimization Phase for CycleTrackerId: {CycleTrackerId}", cycleTrackerId);
            var batchRequest = new OptimizationBatchRequest
            {
                SessionId = session.SessionId,
                StrategyName = session.StrategyName,
                Mode = Data.Models.Backtest.OptimizationMode.FineTune,
                InSampleStartDate = inSampleStartDate,
                InSampleEndDate = inSampleEndDate,
                Symbols = activeUniverse,
                Interval = "60min",
            };
            await _conductorClient.DispatchOptimizationBatchAsync(batchRequest);
            _logger.LogInformation("PHASE 2: Batch dispatch request sent to Conductor for optimization. This job is now complete.");
        }
    }
}
