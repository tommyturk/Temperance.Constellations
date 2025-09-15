using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface IPortfolioBacktestRunner
    {
        Task ExecuteBacktest(Guid sessionId, DateTime oosStartDate);
    }
}
