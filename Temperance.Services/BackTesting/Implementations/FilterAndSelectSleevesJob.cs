using Hangfire;
using Microsoft.Extensions.Logging;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class FilterAndSelectSleevesJob
    {
        private readonly ITradeService _tradeService;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<FilterAndSelectSleevesJob> _logger;

        private const double MinInSampleSharpeRatio = 0.7;
        private const double MaxInSampleDrawdown = 0.25;
        private const int MinInSampleTrades = 12;

        public FilterAndSelectSleevesJob(ITradeService tradeService, IBackgroundJobClient backgroundJobClient, ILogger<FilterAndSelectSleevesJob> logger)
        {
            _tradeService = tradeService;
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 2)]
        public async Task Execute(Guid sessionId, DateTime tradingPeriodStartDate, DateTime inSampleStartDate, DateTime inSampleEndDate)
        {
            _logger.LogInformation("Starting sleeve filtering for SessionId {SessionId} and trading period {TradingPeriod}", sessionId, tradingPeriodStartDate.ToShortDateString());

            // 1. Get all the in-sample verification backtest results for this session
            // This requires a new method in your repository to fetch runs based on session and date range
            var verificationRuns = await _tradeService.GetBacktestRunsForSessionAsync(sessionId, inSampleStartDate, inSampleEndDate);

            // 2. Apply filtering rules
            var qualifiedRuns = verificationRuns
                .Where(r => r.SharpeRatio >= MinInSampleSharpeRatio &&
                            r.MaxDrawdown <= MaxInSampleDrawdown &&
                            r.TotalTrades >= MinInSampleTrades)
                .ToList();

            _logger.LogInformation("Session {SessionId}: Found {QualifiedCount} qualified strategies out of {TotalCount} optimized.", sessionId, qualifiedRuns.Count, verificationRuns.Count());

            if (!qualifiedRuns.Any())
            {
                _logger.LogWarning("Session {SessionId}: No strategies passed the filter. Skipping this trading period.", sessionId);
                // Even if no trades happen, we must advance the cycle
                _backgroundJobClient.Enqueue<MasterWalkForwardOrchestrator>(
                   job => job.ExecuteCycle(sessionId, tradingPeriodStartDate.AddYears(1))
               );
                return;
            }

            // 3. Create sleeve objects from the qualified runs
            var sleeves = qualifiedRuns.Select(run => new WalkForwardSleeve
            {
                SessionId = sessionId,
                TradingPeriodStartDate = tradingPeriodStartDate,
                Symbol = run.Symbols.First(), // Assumes one symbol per run
                Interval = run.Intervals.First(), // Assumes one interval
                StrategyName = run.StrategyName,
                OptimizationResultId = run.OptimizationResultId ?? 0,
                InSampleSharpeRatio = run.SharpeRatio,
                InSampleMaxDrawdown = run.MaxDrawdown,
                OptimizedParametersJson = run.ParametersJson
            }).ToList();

            // 4. Save the selected sleeves to the database
            await _tradeService.SaveSleevesAsync(sleeves);

            // 5. Enqueue the final portfolio backtest job for the out-of-sample period
            var outOfSampleEndDate = tradingPeriodStartDate.AddYears(1).AddDays(-1);
            _backgroundJobClient.Enqueue<IBacktestRunner>(
                runner => runner.RunPortfolioBacktest(sessionId, tradingPeriodStartDate, outOfSampleEndDate)
            );
            _logger.LogInformation("Session {SessionId}: Enqueued portfolio backtest with {SleeveCount} sleeves.", sessionId, sleeves.Count);
        }
    }
}