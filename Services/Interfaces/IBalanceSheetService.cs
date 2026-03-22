using Temperance.Ephemeris.Models.Financials;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IBalanceSheetService
    {
        Task<BalanceSheetModel> SecurityBalanceSheet(string symbol);
    }
}
