using Microsoft.Extensions.Logging;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class PerformanceCalculator : IPerformanceCalculator
    {
        private readonly ILogger<PerformanceCalculator> _logger;
        
        public PerformanceCalculator(ILogger<PerformanceCalculator> logger)
        {
            _logger = logger;
        }

        public KellyMetrics CalculateKellyMetrics(IReadOnlyList<TradeSummary> trades)
        {
            var metrics = new KellyMetrics(); // Create an instance of the new class

            if (trades == null || !trades.Any())
                return metrics;

            var completedTrades = trades.Where(t => t.ProfitLoss.HasValue).ToList();
            if (!completedTrades.Any())
                return metrics;

            var winningTrades = completedTrades.Where(t => t.ProfitLoss > 0).ToList();
            var losingTrades = completedTrades.Where(t => t.ProfitLoss < 0).ToList();

            metrics.TotalTrades = completedTrades.Count;
            metrics.WinningTrades = winningTrades.Count;
            metrics.LosingTrades = losingTrades.Count;

            metrics.WinRate = metrics.TotalTrades > 0 ? (decimal)winningTrades.Count / metrics.TotalTrades : 0;

            metrics.AverageWin = winningTrades.Any() ? winningTrades.Sum(t => t.ProfitLoss.GetValueOrDefault()) / winningTrades.Count : 0;
            metrics.AverageLoss = losingTrades.Any() ? losingTrades.Sum(t => Math.Abs(t.ProfitLoss.GetValueOrDefault())) / losingTrades.Count : 0;

            metrics.PayoffRatio = metrics.AverageLoss > 0 ? metrics.AverageWin / metrics.AverageLoss : (metrics.AverageWin > 0 ? decimal.MaxValue : 0);

            if (metrics.PayoffRatio > 0 && metrics.WinRate > 0 && metrics.WinRate < 1)
            {
                decimal lossProbability = 1 - metrics.WinRate;
                decimal numerator = (metrics.PayoffRatio * metrics.WinRate) - lossProbability;

                if (numerator > 0)
                {
                    metrics.KellyFraction = numerator / metrics.PayoffRatio;
                }
            }

            metrics.KellyHalfFraction = metrics.KellyFraction / 2;

            _logger.LogDebug("Calculated Kelly Metrics: Win Rate: {WinRate:P2}, Payoff Ratio: {PayoffRatio:N2}, Kelly: {Kelly:P2}, Kelly/2: {KellyHalf:P2}",
                metrics.WinRate, metrics.PayoffRatio, metrics.KellyFraction, metrics.KellyHalfFraction);

            return metrics;
        }

        public async Task CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital)
        {
            if (result.Trades == null || !result.Trades.Any())
            {
                _logger.LogWarning("No trades provided or trades list is empty for performance calculation.");
                result.TotalProfitLoss = 0;
                result.TotalReturn = 0;
                result.MaxDrawdown = 0;
                result.WinRate = 0;
                result.EquityCurve = new List<KeyValuePair<DateTime, decimal>> { new(result.Configuration?.StartDate ?? DateTime.MinValue, initialCapital) };
                result.TotalTrades = 0;
                result.WinningTrades = 0;
                result.LosingTrades = 0;
                result.PayoffRatio= 0;
                result.KellyFraction = 0;
                result.KellyHalfFraction = 0;
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

            if (!orderedTrades.Any())
            {
                _logger.LogWarning("No completed trades with calculated ProfitLoss for performance metrics.");
                result.TotalProfitLoss = 0;
                result.TotalReturn = 0;
                result.MaxDrawdown = 0;
                result.WinRate = 0;
                result.TotalTrades = 0;
                result.WinningTrades = 0;
                result.LosingTrades = 0;
                result.PayoffRatio = 0;
                result.KellyFraction = 0;
                result.KellyHalfFraction = 0;
                return;
            }

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

            var winningTrades = orderedTrades.Where(t => t.ProfitLoss > 0).ToList();
            var losingTrades = orderedTrades.Where(t => t.ProfitLoss < 0).ToList();

            decimal totalWinningProfit = winningTrades.Sum(t => t.ProfitLoss!.Value);
            decimal totalLosingProfit = losingTrades.Sum(t => t.ProfitLoss!.Value);

            result.WinningTrades = winningTrades.Count;
            result.LosingTrades = losingTrades.Count;
            result.TotalTrades = result.Trades.Count;

            result.WinRate = result.TotalTrades > 0 ? (decimal)winningTrades.Count / result.TotalTrades : 0;

            decimal averageWin = winningTrades.Any() ? totalWinningProfit / winningTrades.Count : 0;
            decimal averageLoss = losingTrades.Any() ? totalLosingProfit / losingTrades.Count : 0;

            result.PayoffRatio = averageLoss != 0 ? averageWin / averageLoss : (averageWin > 0 ? decimal.MaxValue : 0);

            decimal kellyFraction = 0;
            if(result.PayoffRatio > 0 && result.WinRate > 0 && result.WinRate < 1)
            {
                decimal lossProbability = 1 - result.WinRate.Value;
                decimal numerator = (result.PayoffRatio * result.WinRate.Value) - lossProbability;

                if (numerator > 0)
                    kellyFraction = numerator / result.PayoffRatio;
            }

            result.KellyFraction = kellyFraction;
            result.KellyHalfFraction = kellyFraction / 2;

            return;
        }
    }
}
