using Temperance.Constellations.Models.Backtest;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IPortfolioCommitteeService
    {
        Task<List<CandidateSleeve>> HoldPromotionCommitteeAsync(
             Guid sessionId,
             string strategyName,
             string interval,
             DateTime cycleStartDate,
             int maxActivePositions,
             IReadOnlySet<string> allowedUniverse);
    }
}
