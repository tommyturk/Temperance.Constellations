using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Services.Services.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class MasterWalkForwardOrchestrator
    {
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<MasterWalkForwardOrchestrator> _logger;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IConductorService _conductorService;
        private readonly ITradeService _tradeService;

        // --- Configuration Constants ---
        private const int InSampleYears = 2;
        private const int OutOfSampleYears = 1;

        public MasterWalkForwardOrchestrator(
            IBackgroundJobClient backgroundJobClient,
            ILogger<MasterWalkForwardOrchestrator> logger,
            ISecuritiesOverviewService securitiesOverviewService,
            IConductorService conductorService,
            ITradeService tradeService)
        {
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
            _securitiesOverviewService = securitiesOverviewService;
            _conductorService = conductorService;
            _tradeService = tradeService;
        }

        [AutomaticRetry(Attempts = 2)]
        public async Task ExecuteCycle(Guid sessionId, DateTime currentTradingPeriodStart)
        {
            _logger.LogInformation("Master orchestrator executing new cycle for SessionId {SessionId} starting {StartDate}", sessionId, currentTradingPeriodStart);

            var session = await _tradeService.GetSessionAsync(sessionId);
            if (session == null || currentTradingPeriodStart >= session.EndDate)
            {
                _logger.LogInformation("Walk-forward session {SessionId} has completed its full term. Stopping.", sessionId);
                await _tradeService.UpdateBacktestRunStatusAsync(sessionId, "Completed");
                return;
            }

            // 1. Define the next in-sample/out-of-sample windows
            var inSampleEndDate = currentTradingPeriodStart.AddDays(-OutOfSampleYears);
            var inSampleStartDate = inSampleEndDate.AddYears(-InSampleYears).AddDays(1);

            string optimizationMode = (currentTradingPeriodStart == session.StartDate) ? "train" : "fine-tune";

            _logger.LogInformation("Session {SessionId}: In-Sample Period set from {InSampleStart} to {InSampleEnd}", sessionId, inSampleStartDate, inSampleEndDate);

            // 2. Get the Point-in-Time universe of symbols to optimize
            // We pass null for symbols to get the entire universe based on the repository's rules (e.g., market cap > 5B)
            var pitUniverseSymbols = new List<string>();
            await foreach (var security in _securitiesOverviewService.StreamSecuritiesForBacktest(null, new List<string> { "60min" }))
            {
                pitUniverseSymbols.Add(security.Symbol);
            }

            if (!pitUniverseSymbols.Any())
            {
                _logger.LogError("Session {SessionId}: Could not retrieve any symbols for the Point-in-Time universe. Aborting cycle.", sessionId);
                return;
            }
            _logger.LogInformation("Session {SessionId}: Retrieved {SymbolCount} symbols for optimization.", sessionId, pitUniverseSymbols.Count);

            // 3. Call Conductor to dispatch optimization jobs for the entire universe
            await _conductorService.DispatchOptimizationJobsAsync(
                sessionId,
                session.StrategyName,
                "60min", // Assuming a fixed interval for this walk-forward session
                inSampleStartDate,
                inSampleEndDate,
                pitUniverseSymbols,
                optimizationMode
            );
            _logger.LogInformation("Session {SessionId}: Dispatched all optimization jobs via Conductor.", sessionId);

            // 4. Schedule the FilterAndSelectSleevesJob to run after a delay
            // This delay should be long enough for Ludus to process all jobs.
            var filterDelay = TimeSpan.FromHours(8); // This should be configurable
            _backgroundJobClient.Schedule<FilterAndSelectSleevesJob>(
                job => job.Execute(sessionId, currentTradingPeriodStart, inSampleStartDate, inSampleEndDate),
                filterDelay
            );
            _logger.LogInformation("Session {SessionId}: Scheduled sleeve filtering job to run in {Delay}.", sessionId, filterDelay);
        }
    }
}
