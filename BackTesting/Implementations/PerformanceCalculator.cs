using Microsoft.Extensions.Logging;
using Temperance.Constellations.Models;
using Temperance.Constellations.Models.Performance;
using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.BackTesting.Interfaces;

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
            // 1. DUMMY DATA GUARD
            // If the committee passes $0 initial capital, the percentage math will explode.
            // We force a nominal $100k basis for shadow sleeves if $0 is passed.
            if (initialCapital <= 0) initialCapital = 100000m;

            if (result.Trades == null || !result.Trades.Any())
            {
                _logger.LogWarning("No trades provided for performance calculation.");
                ResetResultMetrics(result, initialCapital);
                return;
            }

            // 2. DATA PREPARATION
            var orderedTrades = result.Trades
                .Where(t => t.ExitDate.HasValue && t.ProfitLoss.HasValue)
                .OrderBy(t => t.ExitDate!.Value)
                .ToList();

            if (!orderedTrades.Any())
            {
                ResetResultMetrics(result, initialCapital);
                return;
            }

            // 3. EQUITY CURVE GENERATION
            decimal runningBalance = initialCapital;
            decimal peakBalance = initialCapital;
            decimal maxDrawdownValue = 0;
            var equityCurve = new List<KeyValuePair<DateTime, decimal>> {
                new(result.Configuration?.StartDate ?? orderedTrades.Min(t => t.EntryDate), initialCapital)
            };

            foreach (var trade in orderedTrades)
            {
                runningBalance += trade.ProfitLoss!.Value;
                equityCurve.Add(new KeyValuePair<DateTime, decimal>(trade.ExitDate!.Value, runningBalance));

                if (runningBalance > peakBalance) peakBalance = runningBalance;

                decimal drawdown = peakBalance - runningBalance;
                if (drawdown > maxDrawdownValue) maxDrawdownValue = drawdown;
            }

            // 4. CORE METRICS
            result.TotalTrades = orderedTrades.Count;
            result.TotalProfitLoss = runningBalance - initialCapital;
            result.TotalReturn = result.TotalProfitLoss / initialCapital;
            result.MaxDrawdown = peakBalance != 0 ? maxDrawdownValue / peakBalance : 0;
            result.EquityCurve = equityCurve;

            var winningTrades = orderedTrades.Where(t => t.ProfitLoss > 0).ToList();
            var losingTrades = orderedTrades.Where(t => t.ProfitLoss < 0).ToList();

            result.WinningTrades = winningTrades.Count;
            result.LosingTrades = losingTrades.Count;
            result.WinRate = (decimal)winningTrades.Count / result.TotalTrades;

            decimal averageWin = winningTrades.Any() ? winningTrades.Sum(t => t.ProfitLoss!.Value) / winningTrades.Count : 0;
            decimal averageLoss = losingTrades.Any() ? Math.Abs(losingTrades.Sum(t => t.ProfitLoss!.Value)) / losingTrades.Count : 0;
            result.PayoffRatio = averageLoss != 0 ? averageWin / averageLoss : (averageWin > 0 ? 10.0m : 0);

            // 5. SHARPE RATIO (THE REPAIRED LOGIC)
            decimal rawSharpe = CalculateSharpeRatio(equityCurve, 0.03m);

            // A. The Sign Guard: If PnL is negative, Sharpe MUST be negative.
            if (result.TotalProfitLoss < 0 && rawSharpe > 0) rawSharpe *= -1;
            if (result.TotalProfitLoss > 0 && rawSharpe < 0) rawSharpe *= -1;

            // B. The Sample Size Penalty: 
            // If trades < 5, the Sharpe is statistically noise. We crush it toward zero.
            if (result.TotalTrades < 5)
            {
                rawSharpe *= (result.TotalTrades / 5.0m);
            }

            result.SharpeRatio = rawSharpe;

            // 6. KELLY METRICS
            var kellyMetrics = CalculateKellyMetrics(orderedTrades);
            result.KellyFraction = kellyMetrics.KellyFraction;
            result.KellyHalfFraction = kellyMetrics.KellyHalfFraction;
        }

        // Helper to clean up empty results
        private void ResetResultMetrics(BacktestResult result, decimal capital)
        {
            result.TotalProfitLoss = 0;
            result.TotalReturn = 0;
            result.MaxDrawdown = 0;
            result.WinRate = 0;
            result.SharpeRatio = 0;
            result.TotalTrades = 0;
            result.EquityCurve = new List<KeyValuePair<DateTime, decimal>> { new(DateTime.UtcNow, capital) };
        }

        public decimal CalculateProfitLoss(TradeSummary trade)
        {
            if (!trade.ExitPrice.HasValue || !trade.ExitDate.HasValue)
                return 0;

            decimal pnl;
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
                    ProfitLoss = sleeveResult.TotalProfitLoss,
                    SharpeRatio = sleeveResult.SharpeRatio,
                    TotalTrades = sleeveResult.TotalTrades,
                    WinRate = sleeveResult.WinRate,
                };
                sleeveComponents.Add(sleeveComponent);
            }

            _logger.LogInformation("Calculated performance for {Count} unique symbols from trades in RunId {RunId}",
                sleeveComponents.Count, portfolioBacktestRunId);

            return sleeveComponents;
        }

        private decimal CalculateSharpeRatio(List<KeyValuePair<DateTime, decimal>> equityCurve, decimal riskFreeRate = 0.0m)
        {
            if (equityCurve == null || equityCurve.Count < 2)
            {
                return 0.0m;
            }

            var returns = new List<double>();
            for (int i = 1; i < equityCurve.Count; i++)
            {
                decimal previousEquity = equityCurve[i - 1].Value;
                decimal currentEquity = equityCurve[i].Value;
                if (previousEquity != 0)
                {
                    returns.Add((double)((currentEquity - previousEquity) / previousEquity));
                }
            }

            if (returns.Count < 2)
            {
                return 0.0m;
            }

            decimal averageReturn = (decimal)returns.Average();
            decimal stdDev = (decimal)MathNet.Numerics.Statistics.Statistics.StandardDeviation(returns);

            if (stdDev == 0)
                return 0.0m;

            TimeSpan totalTimeSpan = equityCurve.Last().Key - equityCurve.First().Key;
            int tradingDays = (int)Math.Max(1, totalTimeSpan.TotalDays);
            decimal periodsPerYear = (returns.Count / (decimal)tradingDays) * 252;

            decimal excessReturn = averageReturn - (riskFreeRate / periodsPerYear);
            decimal sharpeRatio = excessReturn / stdDev;
            decimal annualizedSharpeRatio = sharpeRatio * (decimal)(Math.Sqrt((double)periodsPerYear));

            _logger.LogDebug("Sharpe Calculation: Avg Return={AvgRet:P4}, StdDev={StdDev:P4}, Periods/Year={Periods:N2}, Annualized Sharpe={Sharpe:N2}",
                averageReturn, stdDev, periodsPerYear, annualizedSharpeRatio);

            return annualizedSharpeRatio;
        }
    }
}
