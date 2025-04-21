namespace TradingBot.Data.Models.Trading
{
    public class Order
    {
        public int OrderID { get; set; }
        public int TradeID { get; set; }
        public decimal ExecutionPrice { get; set; }
        public DateTime ExecutionTimestamp { get; set; }
        public string OrderStatus { get; set; } = "Pending";
        public int Quantity { get; set; } = 1;
    }
}
