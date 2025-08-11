using Microsoft.Extensions.Logging;
using Temperance.Data.Data.Repositories.Securities.Interfaces;
using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.Securities.SecurityOverview;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class SecuritiesOverviewService : ISecuritiesOverviewService
    {
        private readonly ISecuritiesOverviewRepository _securitiesOverviewRepository;
        private readonly IAlphaVantageService _alphaVantageService;
        private readonly ILogger<SecuritiesOverviewService> _logger;

        public SecuritiesOverviewService(ISecuritiesOverviewRepository securitiesOverviewRepository, IAlphaVantageService alphaVantageService,
            ILogger<SecuritiesOverviewService> logger)
        {
            _securitiesOverviewRepository = securitiesOverviewRepository;
            _alphaVantageService = alphaVantageService;
            _logger = logger; 

            if (_securitiesOverviewRepository == null)
                _logger.LogCritical("FATAL: SecuritiesOverviewService was created, but its ISecuritiesOverviewRepository is NULL!");
            else
                _logger.LogInformation("SecuritiesOverviewService instance created successfully with a valid repository.");
        }

        public async Task<int> GetSecurityId(string symbol)
        {
            return await _securitiesOverviewRepository.GetSecurityId(symbol);
        }

        public async Task<List<string>> GetSecurities()
        {
            return await _securitiesOverviewRepository.GetSecurities();
        }

        [Obsolete("This method is deprecated. Use StreamSecuritiesForBacktest instead.")]
        public async Task<List<SymbolCoverageBacktestModel>> GetSecuritiesForBacktest(List<string> symbols = null, List<string> intervals = null)
        {
            return await _securitiesOverviewRepository.GetSecuritiesForBacktest(symbols, intervals);
        }

        public IAsyncEnumerable<SymbolCoverageBacktestModel> StreamSecuritiesForBacktest(
           List<string> symbols,
           List<string> intervals,
           CancellationToken cancellationToken = default)
        {
            return _securitiesOverviewRepository.StreamSecuritiesForBacktest(symbols, intervals, cancellationToken);
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

        public async Task<SortedDictionary<DateTime, decimal>> GetSharesOutstandingHistoryAsync(string symbol)
        {
            return await _securitiesOverviewRepository.GetSharesOutstandingHistoryAsync(symbol);
        }

        public async Task<Dictionary<string, double>> GetSectorAveragePERatiosAsync()
        {
            return await _securitiesOverviewRepository.GetSectorAveragePERatiosAsync();
        }
    }
}
