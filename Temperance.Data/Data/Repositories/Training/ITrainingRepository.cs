using Temperance.Data.Models.Backtest.Training;

namespace Temperance.Data.Data.Repositories.Training
{
    public interface ITrainingRepository
    {
        Task<List<ModelTrainingStatus>> GetTradeableUniverseAsync(string strategyName, string interval, DateTime currentOosStartDate);
    }
}
