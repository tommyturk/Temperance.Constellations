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

        [HttpPost("select-sleeve")]
        public IActionResult TriggerSleeveSelection([FromQuery] Guid sessionId, [FromQuery] DateTime inSampleEndDate)
        {
            _logger.LogInformation("Received 'Select Sleeve' signal from Conductor for SessionId: {SessionId}", sessionId);
            _backgroundJobClient.Enqueue<ISleeveSelectionOrchestrator>(orchestrator =>
                orchestrator.SelectInitialSleeve(sessionId, inSampleEndDate));
            return Ok("Sleeve selection phase enqueued.");
        }

        [HttpPost("run-portfolio-backtest")]
        public IActionResult TriggerPortfolioBacktest([FromQuery] Guid sessionId, [FromQuery] DateTime oosDate)
        {
            _logger.LogInformation("Received 'Run Portfolio Backtest' signal from Conductor for SessionId: {SessionId}, OOS Date: {OOSDate}", sessionId, oosDate);

            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(orchestrator =>
                orchestrator.ExecuteNextPeriod(sessionId, oosDate));

            return Ok("Portfolio backtest phase enqueued.");
        }
    }
}