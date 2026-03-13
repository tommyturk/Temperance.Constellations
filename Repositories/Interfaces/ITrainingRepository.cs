using Temperance.Constellations.Models.Backtest.Training;

namespace Temperance.Constellations.Repositories.Interfaces
{
    public interface ITrainingRepository
    {
        Task<List<ModelTrainingStatus>> GetTradeableUniverseAsync(string strategyName, string interval, DateTime currentOosStartDate);
    }
}
