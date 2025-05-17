using Hangfire;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TradingApp.src.Core.Models.MeanReversion;
using TradingApp.src.Core.Services.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;
using Temperance.Data.Models.HistoricalPriceData;

namespace Temperance.Services.BackTesting.Implementations
{
    public class BacktestRunner : IBacktestRunner
    {
        private readonly IHistoricalPriceService _historicalPriceService;
        private readonly IStrategyFactory _strategyFactory;
        private readonly ITradeService _tradesService;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly ILogger<BacktestRunner> _logger;

        public BacktestRunner(
            IHistoricalPriceService historicalPriceService,
            IStrategyFactory strategyFactory,
            ITradeService tradesService,
            ISecuritiesOverviewService securitiesOverviewService,
            IPerformanceCalculator performanceCalculator,
            ILogger<BacktestRunner> logger)
        {
            _historicalPriceService = historicalPriceService;
            _strategyFactory = strategyFactory;
            _tradesService = tradesService;
            _securitiesOverviewService = securitiesOverviewService;
            _performanceCalculator = performanceCalculator;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task RunBacktestAsync(BacktestConfiguration config, Guid runId)
        {
            var result = new BacktestResult();

            try
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");

                var allTrades = new ConcurrentBag<TradeSummary>();
                var strategyInstance = _strategyFactory.CreateStrategy(config.StrategyName, config.StrategyParameters);

                if (strategyInstance == null) throw new InvalidOperationException($"Strategy '{config.StrategyName}' not found.");

                strategyInstance.Initialize(config.InitialCapital, config.StrategyParameters);

                int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
                _logger.LogInformation("RunId: {RunId} - Strategy '{StrategyName}' requires minimum lookback of {MinLookback} bars.", runId, config.StrategyName, strategyMinimumLookback);

                var symbolsToTest = (config.Symbols?.Any() ?? false)
                    ? config.Symbols
                    : (await _securitiesOverviewService.GetSecurities() ?? Enumerable.Empty<string>()).ToList();

                if (!symbolsToTest.Any()) throw new InvalidOperationException("No symbols specified or found.");

                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };

                var testCases = symbolsToTest.SelectMany(symbol => config.Intervals.Select(interval => new { Symbol = symbol, Interval = interval }))
                    .ToList();

                _logger.LogInformation($"RunId: {runId} - Processing {testCases.Count} Symbol/Interval combinations.", runId, testCases.Count().ToString());

                await Parallel.ForEachAsync(testCases, parallelOptions, async (testCase, cancellationToken) =>
                {
                    var symbol = testCase.Symbol;
                    var interval = testCase.Interval;
                    var tradesForThisCase = new List<TradeSummary>();
                    try
                    {
                        _logger.LogDebug("RunId: {RunId} - Starting processing for {Symbol} [{Interval}]", runId, symbol, interval);

                        var dataFetchStartDate = config.StartDate.AddYears(-20);
                        _logger.LogDebug("RunId: {RunId} - Attempting to fetch data for {Symbol} [{Interval}] from {FetchStart} to {FetchEnd}", runId, symbol, interval, dataFetchStartDate, config.EndDate);

                        List<HistoricalPriceModel> historicalData = await _historicalPriceService.GetHistoricalPrices(symbol, interval);

                        if (historicalData == null || !historicalData.Any())
                        {
                            _logger.LogWarning("RunId: {RunId} - No historical data found for {Symbol} [{Interval}]", runId, symbol, interval);
                            return;
                        }

                        var orderedData = historicalData.OrderBy(x => x.Timestamp).ToList();
                        _logger.LogDebug("RunId: {RunId} - Total bars loaded for {Symbol} [{Interval}]: {TotalBars}. Full range: {FirstDate} to {LastDate}", runId, symbol, interval, orderedData.Count, orderedData.First().Timestamp, orderedData.Last().Timestamp);

                        var backtestData = orderedData.Where(x => x.Timestamp >= config.StartDate && x.Timestamp <= config.EndDate).ToList();

                        if (!backtestData.Any())
                        {
                            _logger.LogWarning("RunId: {RunId} - No historical data found for {Symbol} [{Interval}] *within* the requested date range {StartDate} to {EndDate}", runId, symbol, interval, config.StartDate, config.EndDate);
                            return;
                        }
                        _logger.LogDebug("RunId: {RunId} - Bars within test range for {Symbol} [{Interval}]: {BarCount}", runId, symbol, interval, backtestData.Count);

                        Position? currentPosition = null;
                        TradeSummary? activeTrade = null;

                        for (int i = 0; i < backtestData.Count; i++)
                        {
                            var currentBar = backtestData[i];

                            var dataWindow = orderedData.Where(x => x.Timestamp <= currentBar.Timestamp).ToList();

                            if (dataWindow.Count < strategyMinimumLookback)
                                continue;

                            SignalDecision signal = strategyInstance.GenerateSignal(currentBar, dataWindow);

                            if (currentPosition != null)
                            {
                                if (strategyInstance.ShouldExitPosition(currentPosition, currentBar, dataWindow))
                                {
                                    var closedTrade = strategyInstance.ClosePosition(activeTrade, currentBar, signal);
                                    allTrades.Add(closedTrade);

                                    await _tradesService.SaveBacktestResults(runId, new BacktestResult { Trades = new List<TradeSummary> { closedTrade } }, symbol, interval);
                                    currentPosition = null;
                                    activeTrade = null;
                                }
                            }

                            if (currentPosition == null && signal != SignalDecision.Hold)
                            {
                                decimal maxTradeAllocation = config.InitialCapital * 0.02m;

                                decimal actualAllocationAmount = strategyInstance.GetAllocationAmount(currentBar, dataWindow, maxTradeAllocation);

                                if (actualAllocationAmount <= 0 && currentBar.ClosePrice < 0)
                                {
                                    _logger.LogWarning("RunId: {RunId} - Invalid allocation amount for {Symbol} [{Interval}] at {Timestamp}. Allocation: {Allocation}", runId, symbol, interval, currentBar.Timestamp, actualAllocationAmount);
                                    continue;
                                }

                                int calculatedQuantity = (int)Math.Round(actualAllocationAmount / currentBar.ClosePrice);

                                if (calculatedQuantity <= 0)
                                {
                                    _logger.LogWarning("RunId: {RunId} - Invalid position size for {Symbol} [{Interval}] at {Timestamp}. Quantity: {Quantity}", runId, symbol, interval, currentBar.Timestamp, calculatedQuantity);
                                    continue;
                                }

                                activeTrade = new TradeSummary
                                {
                                    EntryDate = currentBar.Timestamp,
                                    EntryPrice = currentBar.ClosePrice,
                                    Direction = signal == SignalDecision.Buy ? "Long" : "Short"
                                };

                                currentPosition = new Position
                                {
                                    EntryPrice = currentBar.ClosePrice,
                                    EntryDate = currentBar.Timestamp,
                                    Quantity = calculatedQuantity,
                                    Direction = signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short
                                };

                                await _tradesService.SaveBacktestResults(runId, new BacktestResult { Trades = new List<TradeSummary> { activeTrade } }, symbol, interval);

                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Timestamp: {Timestamp}, Signal: {Signal}",
                                    runId, symbol, interval, currentBar.Timestamp, signal);
                            }

                            if (currentPosition != null && strategyInstance.ShouldExitPosition(currentPosition, currentBar, dataWindow))
                            {
                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Position Closed. Timestamp: {Timestamp}",
                                    runId, symbol, interval, currentBar.Timestamp);
                            }

                            if (currentPosition == null && signal != SignalDecision.Hold)
                            {
                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Position Opened. Timestamp: {Timestamp}, Direction: {Direction}",
                                    runId, symbol, interval, currentBar.Timestamp, currentPosition?.Direction);
                            }
                        }

                        if (currentPosition != null && activeTrade != null)
                        {
                            var lastBar = backtestData.Last();
                            var closedTrade = strategyInstance.ClosePosition(activeTrade, lastBar, SignalDecision.Sell);
                            allTrades.Add(closedTrade);

                            // Save the final closed trade
                            await _tradesService.SaveBacktestResults(runId, new BacktestResult { Trades = new List<TradeSummary> { closedTrade } }, symbol, interval);

                            _logger.LogInformation($"RunId: {runId}, Symbol: {symbol}, Interval: {interval}, Final Position Closed. Timestamp: {lastBar.Timestamp}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "RunId: {RunId} - Error processing {Symbol} [{Interval}]", runId, symbol, interval);
                    }
                });

                result.Trades.AddRange(allTrades);
                result.TotalTrades = allTrades.Count;
                await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
                await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunId: {RunId} - Error during backtest execution", runId);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", ex.Message);
                throw;
            }
        }

        private int CalculatePositionSize(decimal currentBalance, decimal entryPrice, BacktestConfiguration config, ITradingStrategy strategy)
        {
            // Placeholder - Implement actual position sizing logic
            // Examples:
            // 1. Fixed Quantity: return 10; (like original code)
            // 2. Fixed Monetary Amount: return (int)Math.Floor(1000 / entryPrice); // e.g., $1000 worth
            // 3. Percentage of Equity:
            //    decimal riskAmount = currentBalance * 0.01m; // Risk 1% of capital
            //    decimal stopLossPrice = CalculateStopLossPrice(...); // Requires strategy input or fixed %
            //    decimal priceRiskPerShare = Math.Abs(entryPrice - stopLossPrice);
            //    if (priceRiskPerShare == 0) return 1; // Avoid division by zero
            //    return (int)Math.Floor(riskAmount / priceRiskPerShare);

            return 10; // Defaulting to original fixed quantity for now
        }
    }
}
