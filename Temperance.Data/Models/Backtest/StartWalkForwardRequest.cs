namespace Temperance.Data.Models.Backtest
{
    public class StartWalkForwardRequest
    {
        public Guid SessionId { get; set; }
        public DateTime StartDate { get; set; }
    }
}
