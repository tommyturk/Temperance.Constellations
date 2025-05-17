namespace Temperance.Api.Services.Services.Interfaces
{
    public interface IConductorService
    {
        Task<List<string>> GetSecurities();
        Task<bool> UpdateHistoricalPrices(string symbol, string interval);
    }
}
