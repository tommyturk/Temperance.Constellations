using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Trading.Strategies;
using Temperance.Services.Trading.Strategies.MeanReversion.Implementation;
using Temperance.Services.Trading.Strategies.MeanReversion.Implementations;
using Temperance.Services.Trading.Strategies.Momentum;

namespace Temperance.Services.Factories.Implementations
{
    public class StrategyFactory : IStrategyFactory
    {
        private readonly IServiceProvider _serviceProvider; 
        private readonly Dictionary<string, Type> _strategyRegistry;
        private readonly ILogger<StrategyFactory> _logger;

        private void RegisterStrategies()
        {
            _strategyRegistry.Add("MeanReversion_BB_RSI", typeof(MeanReversionStrategy));
            _strategyRegistry.Add("PairsTrading_Cointegration", typeof(PairsTradingStrategy));
            _strategyRegistry.Add("Dual_Momentum", typeof(DualMomentumStrategy));
            _strategyRegistry.Add("Momentum_MACrossover", typeof(MovingAverageCrossoverStrategy));
        }

        public StrategyFactory(IServiceProvider serviceProvider, ILogger<StrategyFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _strategyRegistry = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            RegisterStrategies(); 
            _logger.LogInformation("StrategyFactory initialized with DI.");
        }

        public T? CreateStrategy<T>(
            string strategyName,
            double initialCapital,
            Dictionary<string, object> parameters) where T : class, IBaseStrategy
        {
            if (_strategyRegistry.TryGetValue(strategyName, out var strategyType))
            {
                if (!typeof(T).IsAssignableFrom(strategyType))
                {
                    _logger.LogError("Strategy '{Name}' (type: {Actual}) cannot be assigned to the requested type '{Requested}'.",
                        strategyName, strategyType.Name, typeof(T).Name);
                    return null;
                }

                var strategy = ActivatorUtilities.CreateInstance(_serviceProvider, strategyType) as T;

                if (strategy != null)
                {
                    strategy.Initialize(initialCapital, parameters);

                    _logger.LogDebug("Strategy '{Name}' of type {Type} created and initialized successfully.", strategyName, typeof(T).Name);
                }
                else
                    _logger.LogError("Failed to create or cast an instance of strategy '{StrategyName}'.", strategyName);

                return strategy;
            }

            _logger.LogError("Strategy '{StrategyName}' not found in registry.", strategyName);
            return null;
        }

        public IEnumerable<string> GetAvailableStrategies()
        {
            return _strategyRegistry.Keys.ToList();
        }
    }
}
