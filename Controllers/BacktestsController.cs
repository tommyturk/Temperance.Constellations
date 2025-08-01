﻿using Hangfire;
using Microsoft.AspNetCore.Mvc;
using TradingApp.src.Core.Services.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;
using Temperance.Data.Models.Strategy;
using Temperance.Services.Services.Interfaces;

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
            var runId = Guid.NewGuid();
            await _tradeService.InitializeBacktestRunAsync(configuration, runId);
            string configJson = System.Text.Json.JsonSerializer.Serialize(configuration);
            var jobId = _backgroundJobClient.Enqueue<IBacktestRunner>(runner =>
                                runner.RunBacktest(configJson, runId));
            _logger.LogInformation("Enqueued backtest RunId: {RunId}, Hangfire JobId: {JobId}", runId, jobId);
            return Accepted(new { BacktestRunId = runId, JobId = jobId });
            
        }

        [HttpPost("start-pairs")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartPairsBacktest([FromBody] PairsBacktestConfiguration configuration)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.StrategyName))
                return BadRequest("Invalid configuration: StrategyName is required.");

            var runId = Guid.NewGuid();
            await _tradeService.InitializePairBacktestRunAsync(configuration, runId);

            var jobId = _backgroundJobClient.Enqueue<IBacktestRunner>(runner =>
                           runner.RunPairsBacktest(configuration, runId));

            _logger.LogInformation("Enqueued pairs backtest RunId: {RunId}, Hangfire JobId: {JobId}", runId, jobId);

            return Accepted(new { BacktestRunId = runId, JobId = jobId });
        }

        [HttpPost("start-dual-momentum")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartDualMomentumBacktest([FromBody] DualMomentumBacktestConfiguration configuration)
        {
            if (configuration == null || !configuration.RiskAssetSymbols.Any() || string.IsNullOrWhiteSpace(configuration.SafeAssetSymbol))
                return BadRequest("Invalid configuration: RiskAssetSymbols and SafeAssetSymbol are required.");

            var runId = Guid.NewGuid();
            await _tradeService.InitializeBacktestRunAsync(configuration, runId);

            string configJson = System.Text.Json.JsonSerializer.Serialize(configuration);

            var jobId = _backgroundJobClient.Enqueue<IBacktestRunner>(runner =>
                runner.RunDualMomentumBacktest(configJson, runId));

            _logger.LogInformation("Enqueued Dual Momentum backtest RunId: {RunId}, Hangfire JobId: {JobId}", runId, jobId);

            return Accepted(new { BacktestRunId = runId, JobId = jobId });
        }
    }
}
