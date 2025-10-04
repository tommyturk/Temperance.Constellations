using Hangfire;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        public async Task SelectInitialSleeve(Guid sessionId, DateTime inSampleEndDate)
        {
            _logger.LogInformation("PHASE 2: Selecting initial sleeve for SessionId: {SessionId}", sessionId);
            var inSampleStartDate = inSampleEndDate.AddYears(-2).AddDays(1); // Assuming 2-year training
            var tradingPeriodStartDate = inSampleEndDate.AddDays(1);

            // 1. GET RUNS (Your existing logic)
            var verificationRuns = await _tradeService.GetBacktestRunsForSessionAsync(sessionId, inSampleStartDate, inSampleEndDate);

            // 2. FILTER RUNS (Your existing logic)
            var qualifiedRuns = verificationRuns
                .Where(r => r.SharpeRatio >= MinInSampleSharpeRatio &&
                            r.MaxDrawdown <= MaxInSampleDrawdown &&
                            r.TotalTrades >= MinInSampleTrades)
                .ToList();

            _logger.LogInformation("Found {QualifiedCount} qualified strategies for SessionId {SessionId}.", qualifiedRuns.Count, sessionId);
            if (!qualifiedRuns.Any())
            {
                _logger.LogWarning("No strategies passed the filter for SessionId {SessionId}. Skipping this cycle.", sessionId);
                // Here you would decide how to advance the state machine - perhaps by starting the next major training cycle.
                return;
            }

            // 3. CREATE SLEEVES (Your existing logic)
            var sleeves = qualifiedRuns.Select(run => new WalkForwardSleeve
            {
                SessionId = sessionId,
                TradingPeriodStartDate = tradingPeriodStartDate,
                Symbol = JsonSerializer.Deserialize<List<string>>(run.SymbolsJson).First(),
                Interval = JsonSerializer.Deserialize<List<string>>(run.IntervalsJson).First(),
                StrategyName = run.StrategyName,
                OptimizationResultId = run.OptimizationResultId,
                InSampleSharpeRatio = run.SharpeRatio,
                InSampleMaxDrawdown = run.MaxDrawdown,
                OptimizedParametersJson = run.ParametersJson,
                IsActive = true // These are the selected ones, so they are active by default.
            }).ToList();

            // 4. SAVE SLEEVES (Your existing logic)
            await _tradeService.SaveSleevesAsync(sleeves);

            // 5. ENQUEUE THE NEXT ORCHESTRATOR (The key change)
            var firstOosDate = tradingPeriodStartDate;
            //_backgroundJobClient.Enqueue<IPortfolioBacktestRunner>(
            //    runner => runner.ExecuteBacktest(sessionId, firstOosDate, oosEndDate)
            //);

            _logger.LogInformation("PHASE 2 Complete. Enqueued portfolio backtest orchestrator for SessionId {SessionId} starting {Date}.", sessionId, firstOosDate);
        }

        public async Task ReselectAnnualSleeve(Guid sessionId, DateTime yearEnd)
        {
            // This is where the annual re-selection logic will go.
            _logger.LogInformation("ORCHESTRATOR (ReselectAnnualSleeve): Kicking off for year {Year}.", yearEnd.Year);

            // For now, we'll just continue the loop by enqueuing the next backtest
            var nextOosDate = yearEnd.AddDays(1);
            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(
               orchestrator => orchestrator.ExecuteNextPeriod(sessionId, nextOosDate)
           );
        }
    }
}