using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Temperance.Services.BackTesting.Interfaces;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Constellations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrchestrationController : ControllerBase
    {
        private readonly ILogger<OrchestrationController> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ITradeService _tradeService;

        public OrchestrationController(
            ILogger<OrchestrationController> logger,
            IBackgroundJobClient backgroundJobClient,
            ITradeService tradeService)
        {
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
            _tradeService = tradeService;
        }


        [HttpPost("start")]
        public IActionResult StartWalkForwardOrchestration(
            [FromQuery] Guid sessionId)
        {
            _logger.LogInformation("Received 'Start' signal from Conductor for SessionId: {SessionId}", sessionId);
            _backgroundJobClient.Enqueue<IInitialTrainingOrchestrator>(orchestrator =>
                orchestrator.StartInitialTraining(sessionId));

            return Ok("Initial training phase enqueued.");
        }

        [HttpPost("start-next-cycle")]
        public IActionResult StartNextWalkForwardCycle([FromQuery] Guid sessionId, [FromQuery] DateTime inSampleEndDate)
        {
            _logger.LogInformation("Received 'Select Sleeve' signal from Conductor for SessionId: {SessionId}", sessionId);
            _backgroundJobClient.Enqueue<IMasterWalkForwardOrchestrator>(orchestrator =>
                orchestrator.InitiateCycle(sessionId, inSampleEndDate));
            return Ok("Sleeve selection phase enqueued.");
        }

        [HttpPost("begin-backtest")]
        public IActionResult Start([FromQuery] Guid sessionId, [FromQuery] DateTime oosStartDate, [FromQuery] DateTime oosEndDate)
        {
            _logger.LogInformation("Received 'Begin Backtest' signal from Conductor for SessionId: {SessionId}, OOS Start Date: {OOSStartDate}, OOS End Date: {OOSEndDate}",
                sessionId, oosStartDate, oosEndDate);
            _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(orchestrator =>
                orchestrator.ExecuteCycle(sessionId, oosStartDate, oosEndDate));
            return Ok("Portfolio backtest phase enqueued.");
        }


        //[HttpPost("run-portfolio-backtest")]
        //public IActionResult TriggerPortfolioBacktest([FromQuery] Guid sessionId, [FromQuery] DateTime oosStartDate)
        //{
        //    _logger.LogInformation("Received 'Run Portfolio Backtest' signal from Conductor for SessionId: {SessionId}, OOS Date: {OOSDate}", sessionId, oosStartDate);

        //    _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(orchestrator =>
        //        orchestrator.ExecuteNextPeriod(sessionId, oosStartDate));

        //    return Ok("Portfolio backtest phase enqueued.");
        //}
    }
}