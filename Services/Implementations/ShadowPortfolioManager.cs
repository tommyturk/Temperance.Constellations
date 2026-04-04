using Temperance.Constellations.Services.Interfaces;
using Temperance.Services.Services.Implementations;

namespace Temperance.Constellations.Services.Implementations
{
    public class ShadowPortfolioManager : PortfolioManager, IShadowPortfolioManager
    {
        public ShadowPortfolioManager(ILogger<ShadowPortfolioManager> logger) : base(logger) { }
    }
}
