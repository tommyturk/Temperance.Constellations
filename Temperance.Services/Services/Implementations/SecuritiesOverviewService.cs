using Microsoft.Extensions.Logging;
using Temperance.Data.Data.Repositories.Securities.Interfaces;
using Temperance.Data.Models.HistoricalData;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class SecuritiesOverviewService : ISecuritiesOverviewService
    {
        private readonly ISecuritiesOverviewRepository _securitiesOverviewRepository;
        private readonly IConductorService _conductorService;
        private readonly ILogger<SecuritiesOverviewService> _logger;

        public SecuritiesOverviewService(ISecuritiesOverviewRepository securitiesOverviewRepository, 
            IConductorService conductorService,
            ILogger<SecuritiesOverviewService> logger)
        {
            _securitiesOverviewRepository = securitiesOverviewRepository;
            _conductorService = conductorService;
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

        public async Task<SecuritiesOverview> GetSecurityOverview(string symbol)
        {
            var checkIfExists = await _securitiesOverviewRepository.GetSecurityOverview(symbol);
            if (checkIfExists != null)
                return checkIfExists;

            var securityOverviewData = await _conductorService.GetSecuritiesOverviewData(symbol);
            if (securityOverviewData == null)
                throw new Exception("Could not find security overview data.");

            return securityOverviewData;
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

        public async Task<List<string>> GetUniverseAsOfDateAsync(DateTime asOfDate)
        {
            return await _securitiesOverviewRepository.GetUniverseAsOfDateAsync(asOfDate);
        }
    }
}
