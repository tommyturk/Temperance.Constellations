using System;
using System.Threading.Tasks;
using Temperance.Data.Models.Backtest;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IBacktestRunner
    {
        [Hangfire.JobDisplayName("Run Backtest {0}")]
        Task RunBacktestAsync(string configJson, Guid runId);
    }
}
