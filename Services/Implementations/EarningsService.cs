using Temperance.Ephemeris.Models.Financials;
using Temperance.Ephemeris.Repositories.Financials.Interfaces;
using Temperance.Constellations.Services.Interfaces;
using ILGPU.IR.Values;

namespace Temperance.Services.Services.Implementations
{
    public class EarningsService : IEarningsService
    {
        private readonly IEarningsRepository _earningsRepository;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        public EarningsService(IEarningsRepository earningsRepository, 
            ISecuritiesOverviewService securitiesOverviewService)
        {
            _earningsRepository = earningsRepository;
            _securitiesOverviewService = securitiesOverviewService;
        }

        public async Task<EarningsModel> SecurityEarningsData(string symbol)
        {
            var securityId = await _securitiesOverviewService.GetSecurityId(symbol);

            EarningsModel earningsData = await _earningsRepository.GetSecurityEarningsData(securityId);
            if (earningsData.Quarterly.Any() || earningsData.Annual.Any() )
                return earningsData;

            return earningsData;
        }
    }
}
