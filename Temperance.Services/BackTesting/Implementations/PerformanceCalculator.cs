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
            var metrics = new KellyMetrics();

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

            metrics.WinRate = metrics.TotalTrades > 0 ? winningTrades.Count / metrics.TotalTrades : 0;

            metrics.AverageWin = winningTrades.Any() ? winningTrades.Sum(t => t.ProfitLoss.GetValueOrDefault()) / winningTrades.Count : 0;
            metrics.AverageLoss = losingTrades.Any() ? losingTrades.Sum(t => Math.Abs(t.ProfitLoss.GetValueOrDefault())) / losingTrades.Count : 0;

            metrics.PayoffRatio = metrics.AverageLoss > 0 ? metrics.AverageWin / metrics.AverageLoss : (metrics.AverageWin > 0 ? double.MaxValue : 0);

            if (metrics.PayoffRatio > 0 && metrics.WinRate > 0 && metrics.WinRate < 1)
            {
                double lossProbability = 1 - metrics.WinRate;
                double numerator = (metrics.PayoffRatio * metrics.WinRate) - lossProbability;

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

        public async Task CalculatePerformanceMetrics(BacktestResult result, double initialCapital)
        {
            if (result.Trades == null || !result.Trades.Any())
            {
                _logger.LogWarning("No trades provided or trades list is empty for performance calculation.");
                result.TotalProfitLoss = 0;
                result.TotalReturn = 0;
                result.MaxDrawdown = 0;
                result.WinRate = 0;
                result.EquityCurve = new List<KeyValuePair<DateTime, double>> { new(result.Configuration?.StartDate ?? DateTime.MinValue, initialCapital) };
                result.TotalTrades = 0;
                result.WinningTrades = 0;
                result.LosingTrades = 0;
                result.PayoffRatio= 0;
                result.KellyFraction = 0;
                result.KellyHalfFraction = 0;
                return;
            }

            double runningBalance = initialCapital;
            double peakBalance = initialCapital;
            double maxDrawdownValue = 0; 
            var equityCurve = new List<KeyValuePair<DateTime, double>> { new(result.Configuration?.StartDate ?? result.Trades.Min(t => t.EntryDate), initialCapital) };

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
                equityCurve.Add(new KeyValuePair<DateTime, double>(trade.ExitDate!.Value, runningBalance));

                if (runningBalance > peakBalance)
                    peakBalance = runningBalance;

                double drawdown = peakBalance - runningBalance;
                if (drawdown > maxDrawdownValue)
                    maxDrawdownValue = drawdown;
            }

            result.TotalProfitLoss = runningBalance - initialCapital;
            result.TotalReturn = initialCapital != 0 ? result.TotalProfitLoss / initialCapital : 0;
            result.MaxDrawdown = peakBalance != 0 ? maxDrawdownValue / peakBalance : 0; 
            result.WinRate = result.TotalTrades > 0 ? result.Trades.Count(t => t.ProfitLoss.HasValue && t.ProfitLoss > 0) / result.TotalTrades : 0;
            result.EquityCurve = equityCurve;

            var winningTrades = orderedTrades.Where(t => t.ProfitLoss > 0).ToList();
            var losingTrades = orderedTrades.Where(t => t.ProfitLoss < 0).ToList();

            double totalWinningProfit = winningTrades.Sum(t => t.ProfitLoss!.Value);
            double totalLosingProfit = losingTrades.Sum(t => t.ProfitLoss!.Value);

            result.WinningTrades = winningTrades.Count;
            result.LosingTrades = losingTrades.Count;
            result.TotalTrades = result.Trades.Count;

            result.WinRate = result.TotalTrades > 0 ? (double)winningTrades.Count / result.TotalTrades : 0;

            double averageWin = winningTrades.Any() ? totalWinningProfit / winningTrades.Count : 0;
            double averageLoss = losingTrades.Any() ? totalLosingProfit / losingTrades.Count : 0;

            result.PayoffRatio = averageLoss != 0 ? averageWin / averageLoss : (averageWin > 0 ? double.MaxValue : 0);

            result.SharpeRatio = CalculateSharpeRatio(equityCurve, 0.03);

            var kellyMetrics = CalculateKellyMetrics(orderedTrades);
            result.KellyFraction = kellyMetrics.KellyFraction;
            result.KellyHalfFraction = kellyMetrics.KellyHalfFraction;
        }

        public double CalculateProfitLoss(TradeSummary trade)
        {
            if (!trade.ExitPrice.HasValue || !trade.ExitDate.HasValue)
                return 0;

            double pnl;
            if (trade.Direction == PositionDirection.Long.ToString())
                pnl = (trade.ExitPrice.Value - trade.EntryPrice) * trade.Quantity;
            else 
                pnl = (trade.EntryPrice - trade.ExitPrice.Value) * trade.Quantity;

            return pnl - (trade.TotalTransactionCost ?? 0);
        }

        public async Task<List<SleeveComponent>> CalculateSleevePerformanceFromTradesAsync(
            BacktestResult portfolioSummary,
            Guid sessionId,
            Guid portfolioBacktestRunId)
        {
            var sleeveComponents = new List<SleeveComponent>();

            if (portfolioSummary.Trades == null || !portfolioSummary.Trades.Any())
            {
                _logger.LogWarning("No trades found in portfolio summary for RunId {RunId}.", portfolioBacktestRunId);
                return sleeveComponents;
            }

            var tradesBySymbol = portfolioSummary.Trades
                .Where(t => t.ExitDate.HasValue && t.ProfitLoss.HasValue)
                .GroupBy(t => t.Symbol);

            foreach (var symbolGroup in tradesBySymbol)
            {
                var symbol = symbolGroup.Key;
                var trades = symbolGroup.ToList();

                var sleeveResult = new BacktestResult
                {
                    Trades = trades,
                    Configuration = portfolioSummary.Configuration
                };

                await CalculatePerformanceMetrics(sleeveResult, 0);

                var sleeveComponent = new SleeveComponent
                {
                    SleeveComponentId = Guid.NewGuid(),
                    RunId = portfolioBacktestRunId,
                    SessionId = sessionId,
                    Symbol = symbol,
                    ProfitLoss = (decimal?)sleeveResult.TotalProfitLoss, 
                    SharpeRatio = (decimal?)sleeveResult.SharpeRatio,
                    TotalTrades = sleeveResult.TotalTrades,
                    WinRate = (decimal?)sleeveResult.WinRate,
                };
                sleeveComponents.Add(sleeveComponent);
            }

            _logger.LogInformation("Calculated performance for {Count} unique symbols from trades in RunId {RunId}",
                sleeveComponents.Count, portfolioBacktestRunId);

            return sleeveComponents;
        }

        private double CalculateSharpeRatio(List<KeyValuePair<DateTime, double>> equityCurve, double riskFreeRate = 0.0)
        {
            if (equityCurve == null || equityCurve.Count < 2)
            {
                return 0.0;
            }

            var returns = new List<double>();
            for (int i = 1; i < equityCurve.Count; i++)
            {
                double previousEquity = equityCurve[i - 1].Value;
                double currentEquity = equityCurve[i].Value;
                if (previousEquity != 0)
                {
                    returns.Add((currentEquity - previousEquity) / previousEquity);
                }
            }

            if (returns.Count < 2)
            {
                return 0.0;
            }

            double averageReturn = returns.Average();
            double stdDev = MathNet.Numerics.Statistics.Statistics.StandardDeviation(returns);

            if (stdDev == 0)
                return 0.0;

            TimeSpan totalTimeSpan = equityCurve.Last().Key - equityCurve.First().Key;
            int tradingDays = (int)Math.Max(1, totalTimeSpan.TotalDays); 
            double periodsPerYear = (returns.Count / (double)tradingDays) * 252; 

            double excessReturn = averageReturn - (riskFreeRate / periodsPerYear); 
            double sharpeRatio = excessReturn / stdDev;
            double annualizedSharpeRatio = sharpeRatio * Math.Sqrt(periodsPerYear);

            _logger.LogDebug("Sharpe Calculation: Avg Return={AvgRet:P4}, StdDev={StdDev:P4}, Periods/Year={Periods:N2}, Annualized Sharpe={Sharpe:N2}",
                averageReturn, stdDev, periodsPerYear, annualizedSharpeRatio);

            return annualizedSharpeRatio;
        }
    }
}
