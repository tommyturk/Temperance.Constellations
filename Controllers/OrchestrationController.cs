using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Temperance.Services.BackTesting.Interfaces;

namespace Temperance.Constellations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrchestrationController : ControllerBase
    {
        private readonly ILogger<OrchestrationController> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public OrchestrationController(
            ILogger<OrchestrationController> logger,
            IBackgroundJobClient backgroundJobClient)
        {
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
        }

        /// <summary>
        /// ENTRY POINT: Kicks off the entire 20-year walk-forward process.
        /// </summary>
        [HttpPost("start")]
        public IActionResult StartWalkForwardOrchestration(
            [FromQuery] Guid sessionId, [FromQuery] string strategyName,
            [FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
        {
            _logger.LogInformation("Received 'Start' signal from Conductor for SessionId: {SessionId}", sessionId);
            _backgroundJobClient.Enqueue<IMasterWalkForwardOrchestrator>(orchestrator =>
                orchestrator.StartInitialTrainingPhase(sessionId, strategyName, startDate, endDate));
            return Ok("Initial training phase enqueued.");
        }

        /// <summary>
        /// STATE TRANSITION: Called by Conductor when the initial training batch is complete.
        /// </summary>
        [HttpPost("select-sleeve")]
        public IActionResult TriggerSleeveSelection([FromQuery] Guid sessionId, [FromQuery] DateTime inSampleEndDate)
        {
            _logger.LogInformation("Received 'Select Sleeve' signal from Conductor for SessionId: {SessionId}", sessionId);
            _backgroundJobClient.Enqueue<ISleeveSelectionOrchestrator>(orchestrator =>
                orchestrator.SelectInitialSleeve(sessionId, inSampleEndDate));
            return Ok("Sleeve selection phase enqueued.");
        }

        /// <summary>
        /// STATE TRANSITION: Called by Conductor when a fine-tuning batch is complete.
        /// </summary>
        [HttpPost("run-portfolio-backtest")]
        public IActionResult TriggerPortfolioBacktest([FromQuery] Guid sessionId, [FromQuery] DateTime oosDate)
        {
            _logger.LogInformation("Received 'Run Portfolio Backtest' signal from Conductor for SessionId: {SessionId}, OOS Date: {OOSDate}", sessionId, oosDate);

            // CORRECTED: Call the method on the orchestrator interface we designed.
            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(orchestrator =>
                orchestrator.ExecuteNextPeriod(sessionId, oosDate));

            return Ok("Portfolio backtest phase enqueued.");
        }

        // The old [HttpPost("start-walk-forward")] endpoint has been removed as it is now redundant.
    }
}