using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.Backtest;
using Temperance.Services.BackTesting.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class PerformanceCalculator : IPerformanceCalculator
    {
        public async Task CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital)
        {
            if (result.Trades == null || !result.Trades.Any())
            {
                result.TotalProfitLoss = 0;
                result.TotalReturn = 0;
                result.MaxDrawdown = 0;
                result.WinRate = 0;
                result.EquityCurve = new List<KeyValuePair<DateTime, decimal>> { new(result.Configuration?.StartDate ?? DateTime.MinValue, initialCapital) };
                return;
            }

            decimal runningBalance = initialCapital;
            decimal peakBalance = initialCapital;
            decimal maxDrawdownValue = 0; 
            var equityCurve = new List<KeyValuePair<DateTime, decimal>> { new(result.Configuration?.StartDate ?? result.Trades.Min(t => t.EntryDate), initialCapital) };

            var orderedTrades = result.Trades
                                    .Where(t => t.ExitDate.HasValue && t.ProfitLoss.HasValue)
                                    .OrderBy(t => t.ExitDate!.Value)
                                    .ToList();

            foreach (var trade in orderedTrades)
            {
                runningBalance += trade.ProfitLoss!.Value;
                equityCurve.Add(new KeyValuePair<DateTime, decimal>(trade.ExitDate!.Value, runningBalance));

                if (runningBalance > peakBalance)
                    peakBalance = runningBalance;

                decimal drawdown = peakBalance - runningBalance;
                if (drawdown > maxDrawdownValue)
                    maxDrawdownValue = drawdown;
            }

            result.TotalProfitLoss = runningBalance - initialCapital;
            result.TotalReturn = initialCapital != 0 ? result.TotalProfitLoss / initialCapital : 0;
            result.MaxDrawdown = peakBalance != 0 ? maxDrawdownValue / peakBalance : 0; 
            result.WinRate = result.TotalTrades > 0 ? result.Trades.Count(t => t.ProfitLoss.HasValue && t.ProfitLoss > 0) / (decimal)result.TotalTrades : 0;
            result.EquityCurve = equityCurve;

            return;
            // TODO: Add more metrics (Sharpe Ratio, Sortino Ratio, etc.)
            // These often require risk-free rate data, benchmark data, and potentially daily equity values.

            // No explicit return needed for Task methods that complete successfully
        }
    }
}
