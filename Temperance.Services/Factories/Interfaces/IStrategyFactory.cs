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
        ITradingStrategy? CreateStrategy(string strategyName, Dictionary<string, object> parameters);
        IEnumerable<string> GetAvailableStrategies();
    }
}
