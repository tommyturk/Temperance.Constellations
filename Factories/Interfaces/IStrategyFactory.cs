using Temperance.Services.Trading.Strategies;

namespace Temperance.Services.Factories.Interfaces
{
    public interface IStrategyFactory
    {
        T? CreateStrategy<T>(
            string strategyName,
            decimal initialCapital,
            Dictionary<string, object> parameters) where T : class, IBaseStrategy;

        IEnumerable<string> GetAvailableStrategies();
    }
}
