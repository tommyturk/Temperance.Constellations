using Hangfire;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class ShadowBacktestOrchestrator : IShadowBacktestOrchestrator
    {
        private const int MaxDegreeOfParallelism = 8;

        private readonly ILogger<ShadowBacktestOrchestrator> _logger;
        private readonly ISingleSecurityBacktester _singleSecurityBacktester;
        private readonly IPerformanceRepository _performanceRepo;
        private readonly IWalkForwardRepository _walkForwardRepo;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public ShadowBacktestOrchestrator(
            ILogger<ShadowBacktestOrchestrator> logger,
            ISingleSecurityBacktester singleSecurityBacktester,
            IPerformanceRepository performanceRepo,
            IWalkForwardRepository walkForwardRepo,
            IBackgroundJobClient backgroundJobClient)
        {
            _logger = logger;
            _singleSecurityBacktester = singleSecurityBacktester;
            _performanceRepo = performanceRepo;
            _walkForwardRepo = walkForwardRepo;
            _backgroundJobClient = backgroundJobClient;
        }

        public async Task Execute(Guid cycleTrackerId, Guid sessionId, Guid runId, List<string> shadowUniverse, DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Starting Parallel Shadow Backtest for RunId {RunId} with {Count} securities. Max Parallelism: {MaxDOP}",
                    runId, shadowUniverse.Count, MaxDegreeOfParallelism);

                var session = await _walkForwardRepo.GetSessionAsync(sessionId);

                var allPerformanceResults = new ConcurrentBag<ShadowPerformance>();
                using var semaphore = new SemaphoreSlim(MaxDegreeOfParallelism);
                var backtestTasks = shadowUniverse.Select(async symbol =>
                {
                    await semaphore.WaitAsync();
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
                                WinRate = summary.WinRate,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error running single security backtest for {Symbol} in RunId {RunId}", symbol, runId);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(backtestTasks);

                if (!allPerformanceResults.IsEmpty)
                {
                    await _performanceRepo.SaveShadowPerformanceAsync(allPerformanceResults.ToList());
                    _logger.LogInformation("Saved {Count} shadow performance results for RunId {RunId}.", allPerformanceResults.Count, runId);
                }
                else
                {
                    _logger.LogWarning("No performance results were generated for Shadow Backtest RunId {RunId}", runId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred during Shadow Backtest for RunId {RunId}", runId);
            }
            finally
            {
                _backgroundJobClient.Enqueue<IMasterWalkForwardOrchestrator>(
                    master => master.SignalBacktestCompletion(cycleTrackerId, BacktestType.Shadow));
            }
        }
    }
}
