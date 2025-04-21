using System;
using System.Threading.Tasks;
using TradingBot.Data.Models.Backtest;

namespace TradingBot.Services.BackTesting.Interfaces
{
    public interface IBacktestRunner
    {
        [Hangfire.JobDisplayName("Run Backtest {0}")]
        Task RunBacktestAsync(BacktestConfiguration config, Guid runId);
    }
}
