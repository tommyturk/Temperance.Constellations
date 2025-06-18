using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Services.Trading.Strategies;

namespace Temperance.Services.Factories.Interfaces
{
    public interface IStrategyFactory
    {
        T? CreateStrategy<T>(
            string strategyName,
            double initialCapital,
            Dictionary<string, object> parameters) where T : class, IBaseStrategy;

        IEnumerable<string> GetAvailableStrategies();
    }
}
