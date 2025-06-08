using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Temperance.Data.Models.Backtest;
using Temperance.Services.Services.Implementations;

namespace Temperance.Constellations.Controllers
{
    [ApiController]
    [Route("api/ai-agent")]
    public class AiAgentController : ControllerBase
    {
        private readonly ILogger<AiAgentController> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public AiAgentController(
            ILogger<AiAgentController> logger,
            IBackgroundJobClient backgroundJobClient)
        {
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
        }

        [HttpPost("luminara-sector")]
        [ProducesResponseType(typeof(object), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult TriggerTradingCycle()
        {
            var runId = Guid.NewGuid();
            _logger.LogInformation("Manual trigger for AI Trading Cycle received. Assigning RunId: {RunId}", runId);

            var backtestConfiguration = new BacktestConfiguration()
            {
                StrategyName = "AITradingStrategy",
                StrategyParameters = new Dictionary<string, object>
                {
                    { "BasePromptFile", "Prompts/claude_papertrading_prompt.txt" },
                    { "TargetSymbols", new List<string> { "AAPL", "MSFT", "GOOGL", "TSLA", "NVDA" } },
                    { "MaxAllocationPerTradePct", 0.10m }
                }
            };

            var jobId = _backgroundJobClient.Enqueue<AiTradingOrchestrator>(
                orchestrator => orchestrator.LuminaraSectorStrategy(runId)
            );

            _logger.LogInformation("Enqueued AI Trading Cycle with RunId: {RunId}, Hangfire JobId: {JobId}", runId, jobId);
            return Accepted(new { Message = "AI Trading Cycle enqueued.", BacktestRunId = runId, JobId = jobId });
        }
    }
}