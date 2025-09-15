using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class MasterWalkForwardOrchestrator : IMasterWalkForwardOrchestrator
    {
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<MasterWalkForwardOrchestrator> _logger;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IConductorService _conductorService;
        private readonly ITradeService _tradeService;
        private readonly IConductorClient _conductorClient;

        // --- Configuration Constants ---
        private const int InSampleYears = 2;
        private const int OutOfSampleYears = 1;

        public object OptimizationMode { get; private set; }

        public MasterWalkForwardOrchestrator(
            IBackgroundJobClient backgroundJobClient,
            ILogger<MasterWalkForwardOrchestrator> logger,
            ISecuritiesOverviewService securitiesOverviewService,
            IConductorService conductorService,
            ITradeService tradeService,
            IConductorClient conductorClient)
        {
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
            _securitiesOverviewService = securitiesOverviewService;
            _conductorService = conductorService;
            _tradeService = tradeService;
            _conductorClient = conductorClient;
        }

        public async Task StartInitialTrainingPhase(Guid sessionId, string strategyName, DateTime startDate, DateTime endDate)
        {
            _logger.LogInformation("PHASE 1: Starting Initial Bulk Training for SessionId: {SessionId}", sessionId);

            var inSampleStartDate = startDate;
            var inSampleEndDate = endDate;

            // Get the universe of all securities active at the start of the period
            var universe = new List<string>();
            await foreach (var security in _securitiesOverviewService.StreamSecuritiesForBacktest(null, new List<string> { "60min" }))
                universe.Add(security.Symbol);

            var symbols = universe.ToList();

            if (!symbols.Any())
            {
                _logger.LogError("No securities found for the initial training period. Aborting walk-forward session {SessionId}.", sessionId);
                // Here you would also update the session status to "Failed" via the ConductorClient
                return;
            }

            _logger.LogInformation("Found {SymbolCount} securities for initial training.", symbols.Count);

            // Create the batch request to dispatch to Ludus via Conductor
            var batchRequest = new OptimizationBatchRequest
            {
                SessionId = sessionId,
                StrategyName = strategyName,
                Mode = Data.Models.Backtest.OptimizationMode.Train,
                InSampleStartDate = inSampleStartDate,
                InSampleEndDate = inSampleEndDate,
                Symbols = symbols,
                Interval = "60min",
            };

            await _conductorClient.DispatchOptimizationBatchAsync(batchRequest);

            _logger.LogInformation("PHASE 1: Batch dispatch request sent to Conductor for {SymbolCount} securities. This job is now complete.", symbols.Count);
        }

        //[AutomaticRetry(Attempts = 2)]
        //public async Task ExecuteCycle(Guid sessionId, DateTime currentTradingPeriodStart)
        //{
        //    _logger.LogInformation("Master orchestrator executing new cycle for SessionId {SessionId} starting {StartDate}", 
        //        sessionId, currentTradingPeriodStart);

        //    var session = await _tradeService.GetSessionAsync(sessionId);
        //    if (session == null || currentTradingPeriodStart >= session.EndDate)
        //    {
        //        _logger.LogInformation("Walk-forward session {SessionId} has completed its full term. Stopping.", sessionId);
        //        await _tradeService.UpdateBacktestRunStatusAsync(sessionId, "Completed");
        //        return;
        //    }

        //    // 1. Define the next in-sample/out-of-sample windows
        //    var inSampleEndDate = currentTradingPeriodStart.AddDays(-OutOfSampleYears);
        //    var inSampleStartDate = inSampleEndDate.AddYears(-InSampleYears).AddDays(1);

        //    string optimizationMode = (currentTradingPeriodStart == session.StartDate) ? "train" : "fine-tune";

        //    _logger.LogInformation("Session {SessionId}: In-Sample Period set from {InSampleStart} to {InSampleEnd}", 
        //        sessionId, inSampleStartDate, inSampleEndDate);

        //    // 2. Get the Point-in-Time universe of symbols to optimize
        //    // We pass null for symbols to get the entire universe based on the repository's rules (e.g., market cap > 5B)
        //    var pitUniverseSymbols = new List<string>();
        //    await foreach (var security in _securitiesOverviewService.StreamSecuritiesForBacktest(null, new List<string> { "60min" }))
        //    {
        //        pitUniverseSymbols.Add(security.Symbol);
        //    }

        //    if (!pitUniverseSymbols.Any())
        //    {
        //        _logger.LogError("Session {SessionId}: Could not retrieve any symbols for the Point-in-Time universe. Aborting cycle.", sessionId);
        //        return;
        //    }
        //    _logger.LogInformation("Session {SessionId}: Retrieved {SymbolCount} symbols for optimization.", sessionId, pitUniverseSymbols.Count);

        //    // 3. Call Conductor to dispatch optimization jobs for the entire universe
        //    await _conductorService.DispatchOptimizationJobsAsync(
        //        sessionId,
        //        session.StrategyName,
        //        "60min", // Assuming a fixed interval for this walk-forward session
        //        inSampleStartDate,
        //        inSampleEndDate,
        //        pitUniverseSymbols,
        //        optimizationMode
        //    );
        //    _logger.LogInformation("Session {SessionId}: Dispatched all optimization jobs via Conductor.", sessionId);

        //    // 4. Schedule the FilterAndSelectSleevesJob to run after a delay
        //    // This delay should be long enough for Ludus to process all jobs.
        //    var filterDelay = TimeSpan.FromHours(8); // This should be configurable
        //    _backgroundJobClient.Schedule<FilterAndSelectSleevesJob>(
        //        job => job.Execute(sessionId, currentTradingPeriodStart, inSampleStartDate, inSampleEndDate),
        //        filterDelay
        //    );
        //    _logger.LogInformation("Session {SessionId}: Scheduled sleeve filtering job to run in {Delay}.", sessionId, filterDelay);
        //}
    }
}
