using System.Text.Json.Serialization;
using Temperance.Data.Models.Trading;

namespace Temperance.Data.Models.Backtest
{
    public class BacktestResult
    {
        public Guid RunId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] // Don't return config unless requested
        public BacktestConfiguration? Configuration { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed

        public List<TradeSummary> Trades { get; set; } = new List<TradeSummary>();
        public int TotalTrades { get; set; }
        public decimal? TotalProfitLoss { get; set; } // Added raw P/L
        public decimal? TotalReturn { get; set; } // As percentage
        public decimal? MaxDrawdown { get; set; } // As percentage
        public decimal? WinRate { get; set; } // As percentage
        public string? ErrorMessage { get; set; } // Store errors

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] // Avoid large payload unless requested
        public List<KeyValuePair<DateTime, decimal>>? EquityCurve { get; set; }

        // Add other aggregated metrics: AvgWin, AvgLoss, ProfitFactor, Sharpe, Sortino etc.
    }
}
