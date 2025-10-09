using Microsoft.Extensions.Logging;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class ShadowBacktestOrchestrator : IShadowBacktestOrchestrator
    {
        private readonly ILogger<ShadowBacktestOrchestrator> _logger;
        private readonly ISingleSecurityBacktester _singleSecurityBacktester;
        private readonly IPerformanceRepository _performanceRepo;
        private readonly IWalkForwardRepository _walkForwardRepo;

        public ShadowBacktestOrchestrator(
            ILogger<ShadowBacktestOrchestrator> logger,
            ISingleSecurityBacktester singleSecurityBacktester,
            IPerformanceRepository performanceRepo,
            IWalkForwardRepository walkForwardRepo)
        {
            _logger = logger;
            _singleSecurityBacktester = singleSecurityBacktester;
            _performanceRepo = performanceRepo;
            _walkForwardRepo = walkForwardRepo;
        }

        public async Task Execute(Guid sessionId, Guid runId, List<string> shadowUniverse, DateTime startDate, DateTime endDate)
        {
            _logger.LogInformation("Starting Shadow Backtest for RunId {RunId} with {Count} securities.", runId, shadowUniverse.Count);

            var session = await _walkForwardRepo.GetSessionAsync(sessionId);
            var allPerformanceResults = new List<ShadowPerformance>();

            foreach (var symbol in shadowUniverse)
            {
                try
                {
                    var summary = await _singleSecurityBacktester.RunAsync(session, symbol, startDate, endDate);
                    if (summary != null)
                    {
                        allPerformanceResults.Add(new ShadowPerformance
                        {
                            RunId = runId,
                            Symbol = summary.Symbol,
                            SharpeRatio = summary.SharpeRatio,
                            ProfitLoss = summary.ProfitLoss,
                            TotalTrades = summary.TotalTrades,
                            WinRate = summary.WinRate
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed shadow backtest for symbol {Symbol} in RunId {RunId}.", symbol, runId);
                }
            }

            if (allPerformanceResults.Any())
            {
                await _performanceRepo.SaveShadowPerformanceAsync(allPerformanceResults);
                _logger.LogInformation("Saved {Count} shadow performance results for RunId {RunId}.", allPerformanceResults.Count, runId);
            }
        }
    }
}
