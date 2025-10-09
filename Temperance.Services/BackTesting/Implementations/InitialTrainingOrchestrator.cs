using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class InitialTrainingOrchestrator : IInitialTrainingOrchestrator
    {
        private readonly ILogger<InitialTrainingOrchestrator> _logger;
        private readonly IWalkForwardRepository _walkForwardRepository;
        private readonly ISecuritiesOverviewService _securitiesService;
        private readonly IConductorClient _conductorClient;

        public InitialTrainingOrchestrator(
            ILogger<InitialTrainingOrchestrator> logger,
            IWalkForwardRepository walkForwardRepository,
            ISecuritiesOverviewService securitiesService,
            IConductorClient conductorClient)
        {
            _logger = logger;
            _walkForwardRepository = walkForwardRepository;
            _securitiesService = securitiesService;
            _conductorClient = conductorClient;
        }

        public async Task StartInitialTraining(Guid sessionId)
        {
            _logger.LogInformation("PHASE 1: Starting Initial Bulk Training for SessionId: {SessionId}", sessionId);

            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            if(session == null)
            {
                _logger.LogError("Could not find session {SessionId}. Aborting.", sessionId);
                return;
            }

            var inSampleStartDate = session.StartDate;
            var inSampleEndDate = session.StartDate.AddYears(session.OptimizationWindowYears).AddDays(-1);

            _logger.LogInformation("In-Sample Period: {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}", inSampleStartDate, inSampleEndDate);

            var universe = await _securitiesService.GetSecurities();
            if(!universe.Any())
            {
                _logger.LogError("No securities found for the initial universe. Aborting.");
                return;
            }

            var batchRequest = new OptimizationBatchRequest
            {
                SessionId = sessionId,
                StrategyName = session.StrategyName,
                InSampleStartDate = inSampleStartDate,
                InSampleEndDate = inSampleEndDate,
                Symbols = universe,
                Interval = "60min",
                Mode = OptimizationMode.Train
            };

            await _conductorClient.DispatchOptimizationBatchAsync(batchRequest);
            await _walkForwardRepository.UpdateSessionStatusAsync(sessionId, "Optimizing");

            _logger.LogInformation("PHASE 1 Complete: Dispatched training batch for {Count} securities. This job is now complete.", universe.Count);
        }
    }
}
