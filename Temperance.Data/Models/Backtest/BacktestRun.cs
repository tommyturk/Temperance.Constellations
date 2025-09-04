namespace Temperance.Data.Models.Backtest
{
    public class BacktestRun
    {
        public Guid RunId { get; set; }
        public Guid SessionId { get; set; }
        public int OptimizationResultId { get; set; }
        public string StrategyName { get; set; } = string.Empty;
        public string ParametersJson { get; set; } = string.Empty;
        public string SymbolsJson { get; set; } = string.Empty;
        public string IntervalsJson { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public double InitialCapital { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? TotalProfitLoss { get; set; }
        public double? TotalReturn { get; set; }
        public double? MaxDrawdown { get; set; }
        public double? WinRate { get; set; }
        public int? TotalTrades { get; set; } // Nullable if calculation deferred
        public string? ErrorMessage { get; set; }
        public double? SharpeRatio { get; set; }
    }
}
