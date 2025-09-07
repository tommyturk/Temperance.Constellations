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

        [AutomaticRetry(Attempts = 2)]
        public async Task Execute(Guid sessionId, DateTime tradingPeriodStartDate, DateTime inSampleStartDate, DateTime inSampleEndDate)
        {
            _logger.LogInformation("Starting sleeve filtering for SessionId {SessionId} and trading period {TradingPeriod}", sessionId, tradingPeriodStartDate.ToShortDateString());

            var verificationRuns = await _tradeService.GetBacktestRunsForSessionAsync(sessionId, inSampleStartDate, inSampleEndDate);

            var qualifiedRuns = verificationRuns
                .AsParallel()
                .Where(r => r.SharpeRatio >= MinInSampleSharpeRatio &&
                            r.MaxDrawdown <= MaxInSampleDrawdown &&
                            r.TotalTrades >= MinInSampleTrades)
                .ToList();

            _logger.LogInformation("Session {SessionId}: Found {QualifiedCount} qualified strategies out of {TotalCount} optimized.", sessionId, qualifiedRuns.Count, verificationRuns.Count());

            if (!qualifiedRuns.Any())
            {
                _logger.LogWarning("Session {SessionId}: No strategies passed the filter. Skipping this trading period.", sessionId);
                _backgroundJobClient.Enqueue<MasterWalkForwardOrchestrator>(
                   job => job.ExecuteCycle(sessionId, tradingPeriodStartDate.AddYears(1))
               );
                return;
            }

            var sleeves = qualifiedRuns.Select(run => new WalkForwardSleeve
            {
                SessionId = sessionId,
                TradingPeriodStartDate = tradingPeriodStartDate,
                Symbol =  JsonSerializer.Deserialize<List<string>>(run.SymbolsJson).First(),
                Interval = JsonSerializer.Deserialize<List<string>>(run.IntervalsJson).First(),
                StrategyName = run.StrategyName,
                OptimizationResultId = run.OptimizationResultId,
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