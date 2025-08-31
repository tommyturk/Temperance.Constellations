using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Implementations;

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
