using Temperance.Data.Models.Backtest;

namespace Temperance.Data.Data.Repositories
{
    public interface IOptimizationRepository
    {
        Task<List<OptimizationJob>> GetOptimizationResultForSession(Guid sessionId, DateTime inSampleEndDate);
        Task<IEnumerable<OptimizationResultDto>> GetOptimizationResultsByWindowAsync(string strategyName, string interval, DateTime inSampleStartDate, DateTime inSampleEndDate);
        Task<Dictionary<string, Dictionary<string, object>>> GetOptimizationResultsBySymbolsAsync(string strategyName, string interval, DateTime startDate, DateTime endDate, List<string> symbols);
    }
}
