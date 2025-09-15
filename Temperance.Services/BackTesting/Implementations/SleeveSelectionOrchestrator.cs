using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class SleeveSelectionOrchestrator : ISleeveSelectionOrchestrator
    {
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly IQualityFilterService _qualityFilter;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<SleeveSelectionOrchestrator> _logger;

        public SleeveSelectionOrchestrator(
            IWalkForwardRepository walkForwardRepository,
            IQualityFilterService qualityFilter,
            IBackgroundJobClient hangfireClient,
            ILogger<SleeveSelectionOrchestrator> logger)
        {
            _walkForwardRepository = walkForwardRepository;
            _qualityFilter = qualityFilter;
            _backgroundJobClient = hangfireClient;
            _logger = logger;
        }

        public async Task SelectInitialSleeve(Guid sessionId, DateTime inSampleEndDate)
        {
            _logger.LogInformation("PHASE 2: Selecting initial sleeve for SessionId: {SessionId}", sessionId);

            // 1. Get all results from the initial training batch
            var allSleeves = await _walkForwardRepository.GetSleevesByBatchAsync(sessionId, inSampleEndDate);

            // 2. Apply filtering logic to find the best candidates
            var selectedSymbols = _qualityFilter.SelectBestPerformers(allSleeves, 100); 

            // 3. Update the database to mark the selected sleeves as active
            await _walkForwardRepository.SetActiveSleeveAsync(sessionId, inSampleEndDate, selectedSymbols);
            _logger.LogInformation("Selected {Count} securities for the initial active sleeve.", selectedSymbols.Count);

            // 4. Enqueue the very first monthly backtest job, starting Feb 1, 2002
            var firstOosDate = new DateTime(2002, 2, 1);
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
