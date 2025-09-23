using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces;
using Temperance.Utilities.Helpers;

namespace Temperance.Services.BackTesting.Implementations
{
    public class SleeveSelectionOrchestrator : ISleeveSelectionOrchestrator
    {
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly IQualityFilterService _qualityFilter;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IOptimizationKeyGenerator _keyGenerator;
        private readonly ILogger<SleeveSelectionOrchestrator> _logger;

        public SleeveSelectionOrchestrator(
            IWalkForwardRepository walkForwardRepository,
            IQualityFilterService qualityFilter,
            IBackgroundJobClient hangfireClient,
            IOptimizationKeyGenerator keyGenerator,
            ILogger<SleeveSelectionOrchestrator> logger)
        {
            _walkForwardRepository = walkForwardRepository;
            _qualityFilter = qualityFilter;
            _backgroundJobClient = hangfireClient;
            _keyGenerator = keyGenerator;
            _logger = logger;
        }

        public async Task SelectInitialSleeve(Guid sessionId, DateTime inSampleEndDate)
        {
            _logger.LogInformation("PHASE 2: Selecting initial sleeve for SessionId: {SessionId}", sessionId);

            var completedJobs = await _walkForwardRepository.GetCompletedJobsForSessionAsync(sessionId);

            var validJobs = completedJobs.Where(job => !string.IsNullOrEmpty(job.ResultKey)).ToList();

            if (!validJobs.Any())
            {
                _logger.LogError("No completed jobs with valid optimization results found for session {SessionId}. Cannot create sleeve.", sessionId);
                return;
            }

            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            var inSampleStartDate = session.StartDate;
            var resultKeys = validJobs.Select(job =>
                _keyGenerator.GenerateOptimizationKey(session.StrategyName, job.Symbol, "60min", inSampleStartDate, inSampleEndDate)
            ).ToList();

            var allOptimizationResults = await _walkForwardRepository.GetResultsByKeysAsync(resultKeys);
            var validResults = allOptimizationResults
                .Where(result => result != null && result.Id != null)
                .ToList();
            // 4. Create sleeve entries for ALL results found.
            // The TradingPeriodStartDate is when this sleeve becomes active.
            var tradingPeriodStartDate = inSampleEndDate.AddDays(1);

            var newSleeveEntries = validResults.Select(result => new WalkForwardSleeve
            {
                SessionId = sessionId,
                TradingPeriodStartDate = tradingPeriodStartDate,
                Symbol = result.Symbol,
                Interval = result.Interval,
                StrategyName = result.StrategyName,
                OptimizedParametersJson = result.OptimizedParametersJson,
                OptimizationResultId = result.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await _walkForwardRepository.CreateSleeveBatchAsync(newSleeveEntries);
            _logger.LogInformation("Created and saved {Count} securities for the initial sleeve.", newSleeveEntries.Count);

            var firstOosDate = tradingPeriodStartDate;

            _backgroundJobClient.Enqueue<IPortfolioBacktestRunner>(
                runner => runner.ExecuteBacktest(sessionId, firstOosDate)
            );
            _logger.LogInformation("PHASE 2 Complete. Enqueued first portfolio backtest for {Date:yyyy-MM-dd}.", firstOosDate);
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
