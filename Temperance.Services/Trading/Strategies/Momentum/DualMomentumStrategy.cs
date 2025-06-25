using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Utilities.Helpers;

namespace Temperance.Services.Trading.Strategies.Momentum
{
    public class DualMomentumStrategy : IDualMomentumStrategy
    {
        public string Name => "Dual_Momentum";

        private int _lookbackMonths;

        public DualMomentumStrategy()
        {
        }

        public void Initialize(double initialCapital, Dictionary<string, object> parameters)
        {
            _lookbackMonths = ParameterHelper.GetParameterOrDefault(parameters, "LookbackMonths", 12);
        }

        private double CalculateMomentum(IReadOnlyList<HistoricalPriceModel> data)
        {
            if (data == null || data.Count < 2) return 0;

            var startPrice = data.First().ClosePrice;
            var endPrice = data.Last().ClosePrice;

            if (startPrice == 0) return 0;

            return (endPrice - startPrice) / startPrice;
        }

        public string SelectWinnerAsset(Dictionary<string, IReadOnlyList<HistoricalPriceModel>> riskAssetData)
        {
            string? winner = null;
            double maxMomentum = double.MinValue;

            foreach (var asset in riskAssetData)
            {
                var momentum = CalculateMomentum(asset.Value);
                if (momentum > maxMomentum)
                {
                    maxMomentum = momentum;
                    winner = asset.Key;
                }
            }

            return winner ?? string.Empty;
        }

        public bool ApplyAbsoluteMomentumFilter(IReadOnlyList<HistoricalPriceModel> winnerAssetData, IReadOnlyList<HistoricalPriceModel> safeAssetData)
        {
            var winnerMomentum = CalculateMomentum(winnerAssetData);
            var safeAssetMomentum = CalculateMomentum(safeAssetData);
            return winnerMomentum > safeAssetMomentum;
        }
    }
}
