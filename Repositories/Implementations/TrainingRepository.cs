using Dapper;
using Microsoft.Data.SqlClient;
using Temperance.Constellations.Models.Backtest.Training;

namespace Temperance.Constellations.Repositories.Interfaces.Training
{
    public class TrainingRepository : ITrainingRepository
    {
        private readonly ILogger<TrainingRepository> _logger;
        private readonly string _connectionString;

        public TrainingRepository(ILogger<TrainingRepository> logger, string connectionString)
        {
            _logger = logger;
            _connectionString = connectionString;
        }

        public async Task<List<ModelTrainingStatus>> GetTradeableUniverseAsync(string strategyName, string interval, DateTime currentOosStartDate)
        {
            const string query = $@"
                SELECT *
                FROM ModelTrainingStatus
                WHERE StrategyName = @StrategyName
                  AND Interval = @Interval
                  AND (LastTrainingDate IS NULL OR LastTrainingDate < @CurrentOosStartDate);";

            await using var connection = new SqlConnection(_connectionString);
            return (await connection.QueryAsync<ModelTrainingStatus>(query, new
            {
                StrategyName = strategyName,
                Interval = interval,
                CurrentOosStartDate = currentOosStartDate
            })).ToList();
        }
    }
}
