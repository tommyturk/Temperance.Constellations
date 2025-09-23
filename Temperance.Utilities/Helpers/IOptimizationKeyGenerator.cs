namespace Temperance.Utilities.Helpers
{
    public interface IOptimizationKeyGenerator
    {
        string GenerateOptimizationKey(string strategyName, string symbol, string interval, DateTime startDate, DateTime endDate);
    }
}
