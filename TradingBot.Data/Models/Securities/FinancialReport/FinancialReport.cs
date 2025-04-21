using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Data.Models.Securities.FinancialReport
{
    public class FinancialReport
    {
        public string Symbol { get; set; }
        public List<AnnualReport> AnnualReports { get; set; }
        public List<QuarterlyReport> QuarterlyReports { get; set; }
    }
}
