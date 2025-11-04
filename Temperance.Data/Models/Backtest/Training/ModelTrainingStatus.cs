namespace Temperance.Data.Models.Backtest.Training
{
    public class ModelTrainingStatus
    {
        public int Id { get; set; }
        public string StrategyName { get; set; }
        public string Symbol { get; set; }
        public string Interval { get; set; }
        public DateTime InitialTrainingSent { get; set; }
        public DateTime? LastTrainingDate { get; set; }
    }
}
