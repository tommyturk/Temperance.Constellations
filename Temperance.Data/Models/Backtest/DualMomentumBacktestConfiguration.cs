namespace Temperance.Data.Models.Backtest
{
    public class DualMomentumBacktestConfiguration : BacktestConfiguration
    {
        public List<string> RiskAssetSymbols { get; set; }

        public string SafeAssetSymbol { get; set; }

        public int MomentumLookbackMonths { get; set; } = 12; // 12 months

        public string RebalancingFrequency { get; set; } = "Monthly"; 
    }
}
