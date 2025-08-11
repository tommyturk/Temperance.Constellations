namespace Temperance.Services.Services.Interfaces
{
    public interface IQualityFilterService
    {
        Task<(bool isHighQuality, string reason)> CheckQualityAsync(
            string symbol,
            SecuritiesOverview overviewData,
            Dictionary<string, double> sectorAveragePERatios);
    }
}
