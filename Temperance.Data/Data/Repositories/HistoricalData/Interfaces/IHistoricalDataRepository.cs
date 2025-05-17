namespace Temperance.Data.Data.Repositories.HistoricalData.Interfaces
{
    public interface IHistoricalDataRepository
    {
        Task<bool> UpdateHistoricalPrices(List<Models.HistoricalData.HistoricalData> prices, string symbol, string timeInterval);
    }
}
