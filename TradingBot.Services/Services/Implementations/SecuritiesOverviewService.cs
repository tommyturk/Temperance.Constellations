using TradingBot.Data.Data.Repositories.Securities.Interfaces;
using TradingBot.Data.Models.Securities.SecurityOverview;
using TradingBot.Services.Services.Interfaces;

namespace TradingBot.Services.Services.Implementations
{
    public class SecuritiesOverviewService : ISecuritiesOverviewService
    {
        private readonly ISecuritiesOverviewRepository _securitiesOverviewRepository;
        private readonly IAlphaVantageService _alphaVantageService;

        public SecuritiesOverviewService(ISecuritiesOverviewRepository securitiesOverviewRepository, IAlphaVantageService alphaVantageService)
        {
            _securitiesOverviewRepository = securitiesOverviewRepository;
            _alphaVantageService = alphaVantageService;
        }

        public async Task<int> GetSecurityId(string symbol)
        {
            return await _securitiesOverviewRepository.GetSecurityId(symbol);
        }

        public async Task<List<string>> GetSecurities()
        {
            return await _securitiesOverviewRepository.GetSecurities();
        }

        public async Task<SecuritySearchResponse> SearchSecuritiesAsync(string query)
        {
            return await _alphaVantageService.SearchSecurities(query);
        }

        public async Task<SecuritiesOverview> GetSecurityOverview(string symbol)
        {
            var securityId = await _securitiesOverviewRepository.GetSecurityId(symbol);
            var checkIfExists = await _securitiesOverviewRepository.GetSecurityOverview(symbol);
            if (checkIfExists != null)
                return checkIfExists;

            var securityOverviewData = await _alphaVantageService.GetSecuritiesOverviewData(symbol);
            if (securityOverviewData == null)
                throw new Exception("Could not find security overview data.");

            var saveSuccess = await _securitiesOverviewRepository.UpdateSecuritiesOverview(securityId, securityOverviewData);

            return securityOverviewData;
        }

        public async Task<bool> UpdateSecuritiesOverviewData(int securityId, string symbols)
        {
            var securitiesOverviewData = await _alphaVantageService.GetSecuritiesOverviewData(symbols);
            if (securitiesOverviewData == null)
            {
                Console.WriteLine("Failed to fetch sector data.");
                return false;
            }
            Console.WriteLine($"Fetched company overview data for {securitiesOverviewData.Name}.");
            Console.WriteLine("Saving sector data to the database...");

            var updateSecuritiesOverview = await _securitiesOverviewRepository.UpdateSecuritiesOverview(securityId, securitiesOverviewData);
            if (!updateSecuritiesOverview)
                Console.WriteLine($"Failed to save securities overview data: {symbols}");

            return updateSecuritiesOverview;
        }

        public async Task<bool> DeleteSecurity(string symbol)
        {
            return await _securitiesOverviewRepository.DeleteSecurity(symbol);
        }
    }
}
