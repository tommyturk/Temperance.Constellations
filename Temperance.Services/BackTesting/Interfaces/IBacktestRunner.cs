using System.Threading.Tasks;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Strategy;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IBacktestRunner
    {
        [Hangfire.JobDisplayName("Run Portfolio Backtest {0}")]
        Task<BacktestResult> RunPortfolioBacktest(BacktestConfiguration config, Guid runId);

        [Hangfire.JobDisplayName("Run Backtest {0}")]
        Task RunBacktest(BacktestConfiguration config, Guid runId);

    //    [Hangfire.JobDisplayName("Run Pairs Backtest {0}")]
    //    Task RunPairsBacktest(PairsBacktestConfiguration configuration, Guid runId);

    //    [Hangfire.JobDisplayName("Run Dual Momentum Backtest {0}")]
    //    Task RunDualMomentumBacktest(string configJson, Guid runId);
    }
}
