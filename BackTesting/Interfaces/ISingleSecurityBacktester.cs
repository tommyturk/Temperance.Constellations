using Temperance.Constellations.Models;
using Temperance.Ephemeris.Models.Constellations;

namespace Temperance.Constellations.BackTesting.Interfaces
{
    public interface ISingleSecurityBacktester
    {
        Task<PerformanceSummary> RunAsync(WalkForwardSessionModel session, string symbol, DateTime startDate, DateTime endDate);
    }
}
