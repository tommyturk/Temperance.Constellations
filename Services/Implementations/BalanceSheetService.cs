using Temperance.Ephemeris.Models.Financials;
using Temperance.Ephemeris.Repositories.Financials.Interfaces;
using Temperance.Constellations.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class BalanceSheetService : IBalanceSheetService
    {
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IBalanceSheetRepository _balanceSheetRepository;
        public BalanceSheetService(ISecuritiesOverviewService securitiesOverviewService, IBalanceSheetRepository balanceSheetService)
        {
            _securitiesOverviewService = securitiesOverviewService;
            _balanceSheetRepository = balanceSheetService;
        }

        public async Task<BalanceSheetModel> SecurityBalanceSheet(string symbol)
        {
            var securityId = await _securitiesOverviewService.GetSecurityId(symbol);

            return await _balanceSheetRepository.GetSecurityBalanceSheet(securityId);
        }
    }
}
