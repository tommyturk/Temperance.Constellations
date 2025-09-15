using Temperance.Data.Models.Backtest;

namespace Temperance.Services.Services.Interfaces
{
    public interface IQualityFilterService
    {
        Task<(bool isHighQuality, string reason)> CheckQualityAsync(
            string symbol,
            SecuritiesOverview overviewData,
            Dictionary<string, double> sectorAveragePERatios);

        List<string> SelectBestPerformers(IEnumerable<WalkForwardSleeve> allSleeves, int topN);
    }
}
