using TradingApp.src.Core.Models.MeanReversion;

namespace Temperance.Data.Models.Trading
{
    public class Trade
    {
        public int TradeID { get; set; }
        public int SecurityID { get; set; }
        public string Symbol { get; set; }
        public string Strategy { get; set; } = "Mean Reversion";
        public SignalDecision TradeType { get; set; }
        public decimal SignalPrice { get; set; }
        public decimal? TakeProfitPrice { get; set; }
        public decimal? StopLossPrice { get; set; }
        public DateTime SignalTimestamp { get; set; }
        public string Status { get; set; } = "Pending";
        public decimal? ExitPrice { get; set; } // NEW: Exit price
        public DateTime? ExitTimestamp { get; set; } // NEW: Exit timestamp
        public decimal? ProfitLoss { get; set; } // NEW: Profit or loss for the trade
    }
}
