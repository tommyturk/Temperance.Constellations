using Hangfire;
using System.Threading.Tasks;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Strategy;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IBacktestRunner
    {
        [Hangfire.JobDisplayName("Run Portfolio Backtest {0}")]
        Task<BacktestResult> RunPortfolioBacktest(BacktestConfiguration config, Guid runId);

        [Queue("constellations_backtest")] // Run this on a dedicated, high-power queue
        [AutomaticRetry(Attempts = 1)]
        Task RunPortfolioBacktest(
            Guid sessionId,
            string strategyName,
            DateTime currentOosStartDate,
            DateTime currentOosEndDate,
            Dictionary<string, string> staticParameters, // <Symbol, ParametersJson>
            List<HistoricalPriceModel> oosMarketData,
            DateTime totalEndDate); // Used to enqueue the next cycle

        [Hangfire.JobDisplayName("Run Backtest {0}")]
        Task RunBacktest(BacktestConfiguration config, Guid runId);

        [Hangfire.JobDisplayName("Run Pairs Backtest {0}")]
        Task RunPairsBacktest(PairsBacktestConfiguration configuration, Guid runId);

    //    [Hangfire.JobDisplayName("Run Dual Momentum Backtest {0}")]
    //    Task RunDualMomentumBacktest(string configJson, Guid runId);
    }
}
