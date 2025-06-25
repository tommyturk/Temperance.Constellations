using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Services.Trading.Strategies.Momentum
{
    public interface IDualMomentumStrategy : IBaseStrategy
    {
        string SelectWinnerAsset(Dictionary<string, IReadOnlyList<HistoricalPriceModel>> riskAssetData);
        bool ApplyAbsoluteMomentumFilter(IReadOnlyList<HistoricalPriceModel> winnerAssetData, IReadOnlyList<HistoricalPriceModel> safeAssetData);
    }
}
