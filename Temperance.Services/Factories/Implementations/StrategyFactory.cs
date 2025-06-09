using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Trading.Strategies;
using Temperance.Services.Trading.Strategies.MeanReversion.Implementation;

namespace Temperance.Services.Factories.Implementations
{
    public class StrategyFactory : IStrategyFactory
    {
        private readonly IServiceProvider _serviceProvider; // This provides access to the DI container
        private readonly Dictionary<string, Type> _strategyRegistry;
        private readonly ILogger<StrategyFactory> _logger;


        private void RegisterStrategies()
        {
            _strategyRegistry.Add("MeanReversion_BB_RSI", typeof(MeanReversionStrategy));
        }

        public StrategyFactory(IServiceProvider serviceProvider, ILogger<StrategyFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _strategyRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            RegisterStrategies(); 
            _logger.LogInformation("StrategyFactory initialized with DI.");
        }

        public ISingleAssetStrategy? CreateStrategy(string strategyName, Dictionary<string, object> parameters)
        {
            if(_strategyRegistry.TryGetValue(strategyName, out var strategyType))
            {
                var strategy = (ISingleAssetStrategy?)ActivatorUtilities.CreateInstance(_serviceProvider, strategyType);

                if (strategy == null)
                    _logger.LogError("Failed to create an instance of strategy '{StrategyName}'.", strategyName);
                else
                    _logger.LogDebug("Strategy '{StrategyName}' created successfully.", strategyName);

                return strategy;
            }
            Console.WriteLine($"Error: Strategy '{strategyName}' not found in registry.");
            return null;
        }

        public IEnumerable<string> GetAvailableStrategies()
        {
            return _strategyRegistry.Keys.ToList();
        }
    }
}
