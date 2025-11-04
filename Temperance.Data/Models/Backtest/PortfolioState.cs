namespace Temperance.Data.Models.Backtest
{
    public class PortfolioState
    {
        public Guid SessionId { get; set; }
        public Guid RunId { get; set; }
        public DateTime AsOfDate { get; set; }
        public double? Cash { get; set; }
        public string OpenPositionsJson { get; set; }
    }
}
