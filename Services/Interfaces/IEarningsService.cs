using Temperance.Ephemeris.Models.Financials;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IEarningsService
    {
        Task<EarningsModel> SecurityEarningsData(string query);
    }
}
