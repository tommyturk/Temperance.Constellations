using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Constellations.Services.Interfaces
{
    public interface IGpuIndicatorService
    {
        Task<Dictionary<DateTime, Dictionary<string, decimal>>> CalculateBulkIndicatorsAsync(
            List<PriceModel> prices,
            Dictionary<string, object> parameters);

        decimal[] CalculateAtr(decimal[] high, decimal[] low, decimal[] close, int period);

        decimal[] CalculateSma(decimal[] prices, int period);

        decimal[] CalculateStdDev(decimal[] prices, int period);

        void Dispose();
    }
}
