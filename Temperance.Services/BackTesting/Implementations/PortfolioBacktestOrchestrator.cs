using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories;
using Temperance.Data.Data.Repositories.Training;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Backtest.Training;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Orchestration.Implementations
{
    public class PortfolioBacktestOrchestrator : IPortfolioBacktestOrchestrator
    {
        private readonly ILogger<PortfolioBacktestOrchestrator> _logger;

        private readonly IBacktestRunner _backtestEngine;
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly IPerformanceRepository _performanceRepository;
        private readonly IOptimizationRepository _optimizationRepository;
        private readonly ITradeService _tradeService;
        private readonly ITrainingRepository _trainingRepository;
        private readonly IHistoricalPriceService _historicalPriceService;

        public PortfolioBacktestOrchestrator(
            ILogger<PortfolioBacktestOrchestrator> logger,
            IBacktestRunner backtestEngine,
            IWalkForwardRepository walkForwardRepo,
            IBackgroundJobClient hangfireClient,
            IPerformanceCalculator performanceCalculator,
            IPerformanceRepository performanceRepository,
            IOptimizationRepository optimizationRepository,
            ITradeService tradeService,
            ITrainingRepository trainingRepository,
            IHistoricalPriceService historicalPriceService)
        {
            _backtestEngine = backtestEngine;
            _walkForwardRepository = walkForwardRepo;
            _backgroundJobClient = hangfireClient;
            _performanceCalculator = performanceCalculator;
            _performanceRepository = performanceRepository;
            _optimizationRepository = optimizationRepository;
            _tradeService = tradeService;
            _logger = logger;
            _trainingRepository = trainingRepository;
            _historicalPriceService = historicalPriceService;
        }

        [Queue("constellations")]
        public async Task ExecuteBacktest(Guid sessionId, DateTime startDate, DateTime totalEndDate)
        {
            _logger.LogInformation("Orchestration starting for Session: {SessionId}", sessionId);

            // 1. Load the master session to get its parameters
            WalkForwardSession session = await _walkForwardRepository.GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogError("Could not find WalkForwardSession with Id: {SessionId}", sessionId);
                return;
            }

            // 2. Enqueue the FIRST cycle, starting from the session's TotalStartDate
            _logger.LogInformation("Enqueuing first cycle for Session: {SessionId} from {StartDate}",
                sessionId, session.StartDate);

            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(o =>
                o.ExecuteCycle(sessionId, session.StartDate, totalEndDate));
        }

        [Queue("constellations")]
        public async Task ExecuteCycle(Guid sessionId, DateTime currentOosStartDate, DateTime totalEndDate)
        {
            // 1. Stop Condition: This is the guard clause that ends the recursion.
            if (currentOosStartDate > totalEndDate)
            {
                _logger.LogInformation("All cycles complete for Session: {SessionId}. Final date {EndDate} reached.",
                    sessionId, totalEndDate.ToShortDateString());
                return;
            }

            // 2. Define Cycle Window: Calculate this cycle's OOS period.
            var currentOosEndDate = currentOosStartDate.AddMonths(6).AddDays(-1);
            if (currentOosEndDate > totalEndDate)
            {
                currentOosEndDate = totalEndDate; // Clamp to the final end date
            }

            _logger.LogInformation("Executing Cycle for Session {SessionId}: OOS [{Start} to {End}]",
                sessionId, currentOosStartDate.ToShortDateString(), currentOosEndDate.ToShortDateString());

            // 3. Get Session Info: Load strategy name, interval, etc.
            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            var (strategyName, interval) = (session.StrategyName, session.Interval);

            // 4. Find *Potential* Universe (PIT):
            // Get all symbols that *should* have a model trained as of the start date.
            List<ModelTrainingStatus> trainingStatus = await _trainingRepository.GetTradeableUniverseAsync(
                strategyName, interval, currentOosStartDate);

            var potentialSymbols = trainingStatus.Select(x => x.Symbol).ToList();

            if (!potentialSymbols.Any())
            {
                _logger.LogWarning("No trained models found for {Strategy} as of {Date}. Skipping cycle.",
                    strategyName, currentOosStartDate.ToShortDateString());

                // CRITICAL: Enqueue the *next* cycle to keep the chain going
                var nextCycleStartDate = currentOosEndDate.AddDays(1);
                _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(o =>
                    o.ExecuteCycle(sessionId, nextCycleStartDate, totalEndDate));
                return;
            }

            // 5. Get *Valid* Parameters (PIT):
            // Of the potential symbols, find those that have parameters saved *before* the start date.
            var optimizationResults = await _optimizationRepository.GetLatestParametersAsync(
                strategyName, potentialSymbols, interval, currentOosStartDate);

            // 6. Create *Final* Universe & Parameter Dictionary:
            // This is the definitive list of symbols we will actually trade this cycle.
            var staticParameters = optimizationResults
                .ToDictionary(
                    result => result.Symbol,
                    result => result.OptimizedParametersJson
                );

            // 7. Robustness Check #2:
            // Check if we have any symbols *after* filtering for parameters.
            if (!staticParameters.Any())
            {
                _logger.LogWarning("Models were trained for {Count} symbols, but no valid *parameters* were found before {Date}. Skipping cycle.",
                    potentialSymbols.Count, currentOosStartDate.ToShortDateString());

                // CRITICAL: Enqueue the *next* cycle
                var nextCycleStartDate = currentOosEndDate.AddDays(1);
                _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(o =>
                    o.ExecuteCycle(sessionId, nextCycleStartDate, totalEndDate));
                return;
            }

            // 8. Get OOS Market Data (REFINED):
            // Fetch data *only* for the symbols we are actually trading.
            var finalUniverseSymbols = staticParameters.Keys.ToList();
            _logger.LogInformation("Found {Count} symbols with valid parameters for this cycle.", finalUniverseSymbols.Count);

            var oosMarketData = await _historicalPriceService.GetHistoricalPrices(
                finalUniverseSymbols, // <-- Was: `symbols`
                interval,
                currentOosStartDate,
                currentOosEndDate);

            // 9. Enqueue Runner (REFINED):
            _logger.LogInformation("Enqueuing BacktestRunner with {DataCount} bars for {SymbolCount} symbols.",
                oosMarketData.Count,
                finalUniverseSymbols.Count); // <-- Was: `finalUniverse.Count` (which didn't exist)

            _backgroundJobClient.Enqueue<IBacktestRunner>(r =>
                r.RunPortfolioBacktest(
                    sessionId,
                    strategyName,
                    currentOosStartDate,
                    currentOosEndDate,
                    staticParameters,
                    oosMarketData,
                    totalEndDate // Pass the totalEndDate along for the loop
                ));
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
                        StrategyName = session.StrategyName,
                        SessionId = session.SessionId,
                        StartDate = oosStartDate,
                        EndDate = oosEndDate,
                        InitialCapital = session.CurrentCapital,
                        Symbols = activeUniverse,
                        Intervals = new List<string> { "60min" },
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
                    {
                        await _performanceRepository.SaveSleeveComponentsAsync(sleeveComponents);
                        var profitLoss = sleeveComponents.Sum(s => s.ProfitLoss) ?? 0;
                        await _walkForwardRepository.UpdateCurrentCapital(sessionId, profitLoss);
                    }
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