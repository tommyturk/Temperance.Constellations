using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces; 

namespace Temperance.Services.BackTesting.Orchestration.Implementations
{
    public class FineTuneOrchestrator : IFineTuneOrchestrator
    {
        private readonly IConductorClient _conductorClient;
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly ILogger<FineTuneOrchestrator> _logger;

        public FineTuneOrchestrator(
            IConductorClient conductorClient,
            IWalkForwardRepository walkForwardRepo,
            ILogger<FineTuneOrchestrator> logger)
        {
            _conductorClient = conductorClient;
            _walkForwardRepository = walkForwardRepo;
            _logger = logger;
        }

        public async Task ExecuteFineTune(Guid sessionId, DateTime backtestMonthEndDate)
        {
            _logger.LogInformation("MAIN LOOP: Starting Fine-Tune phase for SessionId {SessionId} after month-end {Date:yyyy-MM-dd}", sessionId, backtestMonthEndDate);

            // 1. Define the 2-month rolling fine-tune period
            var fineTuneEndDate = backtestMonthEndDate;
            var fineTuneStartDate = fineTuneEndDate.AddMonths(-2).AddDays(1);

            // 2. Get the currently active sleeve
            var activeSleeve = await _walkForwardRepository.GetActiveSleeveAsync(sessionId, backtestMonthEndDate);
            var activeSymbols = activeSleeve.Select(s => s.Symbol).ToList();

            var session = await _walkForwardRepository.GetSessionAsync(sessionId);

            // 3. Create the batch request
            var batchRequest = new OptimizationBatchRequest
            {
                SessionId = sessionId,
                StrategyName = session.StrategyName,
                Mode = OptimizationMode.FineTune,
                InSampleStartDate = fineTuneStartDate,
                InSampleEndDate = fineTuneEndDate,
                Symbols = activeSymbols
            };

            // 4. Dispatch the work to Conductor/Ludus
            await _conductorClient.DispatchOptimizationBatchAsync(batchRequest);

            _logger.LogInformation("Dispatched fine-tune batch for {Count} securities. This job is now complete.", activeSymbols.Count);
        }
    }
}