using TradingApp.src.Core.Strategies.MeanReversion.Implementations;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Trading.Strategies;

namespace Temperance.Services.Factories.Implementations
{
    public class StrategyFactory : IStrategyFactory
    {
        private readonly IServiceProvider _serviceProvider;

        private readonly Dictionary<string, Type> _strategyRegistry = new()
        {
            { "MeanReversion_BB_RSI", typeof(MeanReversionStrategy) }
            // Add other strategies here: { "MyOtherStrategy", typeof(MyOtherStrategy) }
        };

        public StrategyFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ITradingStrategy? CreateStrategy(string strategyName, Dictionary<string, object> parameters)
        {
            if (_strategyRegistry.TryGetValue(strategyName, out var strategyType))
            {
                // Use ActivatorUtilities if strategy has constructor dependencies managed by DI
                // return (ITradingStrategy?)ActivatorUtilities.CreateInstance(_serviceProvider, strategyType);

                // If strategies have parameterless constructors:
                return (ITradingStrategy?)Activator.CreateInstance(strategyType, parameters);
            }
            // Log error: Strategy not found
            Console.WriteLine($"Error: Strategy '{strategyName}' not found in registry.");
            return null;
        }

        public IEnumerable<string> GetAvailableStrategies()
        {
            return _strategyRegistry.Keys.ToList();
        }
    }
}
