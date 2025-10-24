using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Orchestration.Implementations
{
    public class PortfolioBacktestOrchestrator : IPortfolioBacktestOrchestrator
    {
        private readonly IBacktestRunner _backtestEngine;
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly IPerformanceRepository _performanceRepository;
        private readonly IOptimizationRepository _optimizationRepository;
        private readonly ITradeService _tradeService;
        private readonly ILogger<PortfolioBacktestOrchestrator> _logger;

        public PortfolioBacktestOrchestrator(
            IBacktestRunner backtestEngine,
            IWalkForwardRepository walkForwardRepo,
            IBackgroundJobClient hangfireClient,
            IPerformanceCalculator performanceCalculator,
            IPerformanceRepository performanceRepository,
            IOptimizationRepository optimizationRepository,
            ITradeService tradeService,
            ILogger<PortfolioBacktestOrchestrator> logger)
        {
            _backtestEngine = backtestEngine;
            _walkForwardRepository = walkForwardRepo;
            _backgroundJobClient = hangfireClient;
            _performanceCalculator = performanceCalculator;
            _performanceRepository = performanceRepository;
            _optimizationRepository = optimizationRepository;
            _tradeService = tradeService;
            _logger = logger;
        }

        public async Task ExecuteNextPeriod(
            Guid cycleTrackerId,
            Guid sessionId,
            Guid portfolioBacktestRunId,
            List<string> activeUniverse,
            DateTime inSampleStartDate,
            DateTime inSampleEndDate,
            DateTime oosStartDate,
            DateTime oosEndDate)
        {
            try
            {
                var session = await _walkForwardRepository.GetSessionAsync(sessionId);
                if (session == null)
                {
                    _logger.LogError("Could not find session {SessionId}. Aborting backtest for Cycle {CycleTrackerId}.",
                        sessionId, cycleTrackerId);
                    return;
                }
                if (!activeUniverse.Any())
                    _logger.LogWarning("ORCHESTRATOR: No active sleeve symbols provided for Cycle {CycleTrackerId}. Skipping backtest.", cycleTrackerId);
                else
                {
                    var sleeveParameters = await _optimizationRepository.GetOptimizationResultsBySymbolsAsync(
                        session.StrategyName,
                        "60min",
                        inSampleStartDate,
                        inSampleEndDate,
                        activeUniverse);

                    var config = new BacktestConfiguration
                    {
                        RunId = portfolioBacktestRunId,
                        SessionId = session.SessionId,
                        StartDate = oosStartDate,
                        EndDate = oosEndDate,
                        InitialCapital = session.CurrentCapital,
                        Symbols = activeUniverse,
                        StrategyName = session.StrategyName,
                        PortfolioParameters = sleeveParameters,
                        MaxParallelism = 16,
                    };
                    _logger.LogInformation("ORCHESTRATOR: Starting OOS Backtest {RunId} for Cycle {CycleTrackerId} with {SymbolCount} symbols.",
                        config.RunId, cycleTrackerId, config.Symbols.Count);

                    await _tradeService.InitializeBacktestRunAsync(config, config.RunId);

                    var portfolioSummary = await _backtestEngine.RunPortfolioBacktest(config, config.RunId);
                    _logger.LogInformation("ORCHESTRATOR: OOS Backtest {RunId} for Cycle {CycleTrackerId} completed.",
                        config.RunId, cycleTrackerId);

                    List<SleeveComponent> sleeveComponents = await _performanceCalculator.CalculateSleevePerformanceFromTradesAsync(
                        portfolioSummary,
                        sessionId,
                        portfolioBacktestRunId);
                    

                    if (sleeveComponents.Any())
                        await _performanceRepository.SaveSleeveComponentsAsync(sleeveComponents);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute portfolio backtest for Cycle {CycleTrackerId}", cycleTrackerId);
            }
            finally
            {
                _logger.LogInformation("Enqueuing completion signal for Portfolio Backtest, Cycle {CycleTrackerId}", cycleTrackerId);

                _backgroundJobClient.Enqueue<IMasterWalkForwardOrchestrator>(
                    master => master.SignalBacktestCompletion(cycleTrackerId, BacktestType.Portfolio));
            }
        }
    }
}