using Temperance.Ephemeris.Models.Financials;
using Temperance.Ephemeris.Repositories.Financials.Interfaces;
using Temperance.Constellations.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class EarningsService : IEarningsService
    {
        private readonly IEarningsRepository _earningsRepository;
        private readonly IAlphaVantageService _alphaVantageService;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        public EarningsService(IEarningsRepository earningsRepository, IAlphaVantageService alphaVantageService,
            ISecuritiesOverviewService securitiesOverviewService)
        {
            _earningsRepository = earningsRepository;
            _alphaVantageService = alphaVantageService;
            _securitiesOverviewService = securitiesOverviewService;
        }

        public async Task<EarningsModel> SecurityEarningsData(string symbol)
        {
            var securityId = await _securitiesOverviewService.GetSecurityId(symbol);

            EarningsModel earningsData = await _earningsRepository.GetSecurityEarningsData(securityId);
            if (earningsData.Quarterly.Any() || earningsData.Annual.Any() )
                return earningsData;

            var getEarningsData = await UpdateEarningsData(securityId, symbol);
            if (getEarningsData == null)
                throw new Exception($"Cannot get Earnings Data for {symbol}");

            return await _earningsRepository.GetSecurityEarningsData(securityId);
        }

        public async Task<bool> UpdateEarningsData(int securityId, string symbol)
        {
            var earningsData = await _alphaVantageService.GetSecuritiesEarningsData(symbol);
            if (earningsData == null)
            {
                Console.WriteLine($"Failed to fetch earnings data...");
                return false;
            }
            Console.WriteLine($"Fetched earnings datafor {symbol}: Years covered: {earningsData?.Annual?.Count}; Quarters Covered: {earningsData?.Quarterly?.Count}");

            var updateEarningsDataSuccess = await _earningsRepository.InsertSecuritiesEarningsData(securityId, earningsData, symbol);
            if (!updateEarningsDataSuccess)
                Console.WriteLine($"Failed to save earnings data: {symbol}");

            return updateEarningsDataSuccess;
        }
    }
}
