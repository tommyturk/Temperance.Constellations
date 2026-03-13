using Temperance.Constellations.Models;
using Temperance.Ephemeris.Models.Financials;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IQualityFilterService
    {
        Task<(bool isHighQuality, string reason)> CheckQualityAsync(
            string symbol,
            SecurityOverviewModel overviewData,
            Dictionary<string, double> sectorAveragePERatios);

        List<string> SelectBestPerformers(IEnumerable<WalkForwardSleeve> allSleeves, int topN);
    }
}
