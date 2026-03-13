using Temperance.Ephemeris.Models.Prices;

namespace Temperance.Services.Trading.Strategies.Momentum
{
    public interface IDualMomentumStrategy : IBaseStrategy
    {
        string SelectWinnerAsset(Dictionary<string, IReadOnlyList<PriceModel>> riskAssetData);
        bool ApplyAbsoluteMomentumFilter(IReadOnlyList<PriceModel> winnerAssetData, IReadOnlyList<PriceModel> safeAssetData);
    }
}
