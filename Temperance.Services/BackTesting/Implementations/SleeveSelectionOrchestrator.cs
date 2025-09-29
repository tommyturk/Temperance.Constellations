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

            // ... (your existing code to get completedJobs, validJobs, resultKeys, allOptimizationResults)

            var bestResultsBySymbol = allOptimizationResults
                .Where(result => result != null && result.Id != null)
                .GroupBy(result => result.Symbol)
                .Select(group => group.First())
                .ToList();

            _logger.LogInformation("Found {Count} unique symbols with optimization results for session {SessionId}.", bestResultsBySymbol.Count, sessionId);

            if (!bestResultsBySymbol.Any())
            {
                _logger.LogError("Could not determine a single best optimization result for any symbol in session {SessionId}.", sessionId);
                return;
            }

            var tradingPeriodStartDate = inSampleEndDate.AddDays(1);

            var existingSleeveSymbols = await _walkForwardRepository.GetSleeveSymbolsForPeriodAsync(sessionId, tradingPeriodStartDate);

            var newResultsToSleeve = bestResultsBySymbol
                .Where(result => !existingSleeveSymbols.Contains(result.Symbol))
                .ToList();

            if (!newResultsToSleeve.Any())
            {
                _logger.LogInformation("All best symbols for this period have already been added to the sleeve. No new sleeves created.");
                var firstOosDate = tradingPeriodStartDate;
                _backgroundJobClient.Enqueue<IPortfolioBacktestRunner>(runner => runner.ExecuteBacktest(sessionId, firstOosDate));
                return;
            }
            // --- END CHECK ---


            var newSleeveEntries = newResultsToSleeve.Select(result => new WalkForwardSleeve
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
            _logger.LogInformation("Selected best parameters and created {Count} new sleeves for the initial portfolio.", newSleeveEntries.Count);

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
