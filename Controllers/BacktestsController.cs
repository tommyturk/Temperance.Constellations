using Hangfire;
using Microsoft.AspNetCore.Mvc;
using TradingApp.src.Core.Services.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;

namespace Temperance.Constellations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BacktestsController : Controller
    {
        private readonly ILogger<BacktestsController> _logger;
        private readonly IStrategyFactory _strategyFactory;
        private readonly ITradeService _tradeService;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public BacktestsController(IStrategyFactory strategyFactory, ITradeService tradeService, IBackgroundJobClient backgroundJobClient, 
            ILogger<BacktestsController> logger)
        {
            _strategyFactory = strategyFactory;
            _tradeService = tradeService;
            _backgroundJobClient = backgroundJobClient;
            _logger = logger;
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartBackTest([FromBody] BacktestConfiguration configuration)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.StrategyName))
                return BadRequest("Invalid configuration: StrategyName is required.");

            if (_strategyFactory.CreateStrategy(configuration.StrategyName, configuration.StrategyParameters) == null)
                return BadRequest($"Invalid configuration: Strategy '{configuration.StrategyName}' not found.");

            var runId = Guid.NewGuid(); 

            await _tradeService.InitializeBacktestRunAsync(configuration, runId);

            var jobId = _backgroundJobClient.Enqueue<IBacktestRunner>(runner =>
                           runner.RunBacktestAsync(configuration, runId));

            _logger.LogInformation("Enqueued backtest RunId: {RunId}, Hangfire JobId: {JobId}", runId, jobId);

            return Accepted(new { BacktestRunId = runId, JobId = jobId });
        }
    }
}
