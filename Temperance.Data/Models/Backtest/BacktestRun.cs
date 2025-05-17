using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Data.Models.Backtest
{
    public class BacktestRun
    {
        public Guid RunId { get; set; }
        public string StrategyName { get; set; } = string.Empty;
        public string ParametersJson { get; set; } = string.Empty;
        public string SymbolsJson { get; set; } = string.Empty;
        public string IntervalsJson { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal InitialCapital { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal? TotalProfitLoss { get; set; }
        public decimal? TotalReturn { get; set; }
        public decimal? MaxDrawdown { get; set; }
        public decimal? WinRate { get; set; }
        public int? TotalTrades { get; set; } // Nullable if calculation deferred
        public string? ErrorMessage { get; set; }
    }
}
