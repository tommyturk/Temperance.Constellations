using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Implementations;
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
            [FromQuery] Guid sessionId,
            [FromQuery] string strategyName,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate)
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
            _backgroundJobClient.Enqueue<IPortfolioBacktestRunner>(runner =>
                runner.ExecuteMonthlyBacktest(sessionId, oosDate));
            return Ok("Portfolio backtest phase enqueued.");
        }

        [HttpPost("start-walk-forward")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult StartWalkForward([FromBody] StartWalkForwardRequest request)
        {
            if (request.SessionId == Guid.Empty)
            {
                return BadRequest("A valid SessionId must be provided.");
            }

            _logger.LogInformation(
                "Received command from Conductor to start walk-forward orchestration for SessionId {SessionId}",
                request.SessionId);

            var jobId = _backgroundJobClient.Enqueue<MasterWalkForwardOrchestrator>(
                orchestrator => orchestrator.ExecuteCycle(request.SessionId, request.StartDate)
            );

            _logger.LogInformation(
                "Enqueued MasterWalkForwardOrchestrator for SessionId {SessionId}. Hangfire JobId: {JobId}",
                request.SessionId,
                jobId);

            return Accepted(new { Message = "Walk-forward orchestration enqueued.", JobId = jobId });
        }
    }
}
