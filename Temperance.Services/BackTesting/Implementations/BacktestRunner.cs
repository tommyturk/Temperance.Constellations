using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Implementations;
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;
using TradingApp.src.Core.Services.Interfaces;

namespace Temperance.Services.BackTesting.Implementations
{
    public class BacktestRunner : IBacktestRunner
    {
        private readonly IHistoricalPriceService _historicalPriceService;
        private readonly ILiquidityService _liquidityService;
        private readonly ITransactionCostService _transactionCostService;
        private readonly IPortfolioManager _portfolioManager;
        private readonly IStrategyFactory _strategyFactory;
        private readonly ITradeService _tradesService;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly ILogger<BacktestRunner> _logger;

        public BacktestRunner(
            IHistoricalPriceService historicalPriceService,
            ILiquidityService liquidityService,
            ITransactionCostService transactionCostService,
            IPortfolioManager portfolioManager,
            IStrategyFactory strategyFactory,
            ITradeService tradesService,
            ISecuritiesOverviewService securitiesOverviewService,
            IPerformanceCalculator performanceCalculator,
            ILogger<BacktestRunner> logger)
        {
            _historicalPriceService = historicalPriceService;
            _liquidityService = liquidityService;
            _transactionCostService = transactionCostService;
            _portfolioManager = portfolioManager;
            _strategyFactory = strategyFactory;
            _tradesService = tradesService;
            _securitiesOverviewService = securitiesOverviewService;
            _performanceCalculator = performanceCalculator;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task RunBacktestAsync(string configJson, Guid runId)
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<BacktestConfiguration>(configJson);
            if (config == null)
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Failed to deserialize configuration.");
                throw new ArgumentException("Could not deserialize backtest configuration from JSON.", nameof(configJson));
            }
            _logger.LogInformation("Successfully deserialized configuration for RunId: {RunId}", runId);

            var result = new BacktestResult();

            try
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");

                await _portfolioManager.Initialize(config.InitialCapital);

                var allTrades = new ConcurrentBag<TradeSummary>();
                var strategyInstance = _strategyFactory.CreateStrategy(config.StrategyName, config.StrategyParameters);
                if (strategyInstance == null)
                    throw new InvalidOperationException($"Strategy '{config.StrategyName}' could not be created.");

                strategyInstance.Initialize(config.InitialCapital, config.StrategyParameters);

                int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
                _logger.LogInformation("RunId: {RunId} - Strategy '{StrategyName}' requires minimum lookback of {MinLookback} bars.", runId, config.StrategyName, strategyMinimumLookback);

                long minimumAdv = strategyInstance.GetMinimumAverageDailyVolume();
                int rollingAdvLookbackBars = 20;
                int rollingKellyLookbackTrades = 50;

                var symbolsToTest = (config.Symbols?.Any() ?? false)
                    ? config.Symbols
                    : (await _securitiesOverviewService.GetSecurities() ?? Enumerable.Empty<string>()).ToList();

                if (!symbolsToTest.Any()) throw new InvalidOperationException("No symbols specified or found for backtest.");

                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };

                var testCases = symbolsToTest.SelectMany(symbol => config.Intervals.Select(interval => new { Symbol = symbol, Interval = interval }))
                    .ToList();

                _logger.LogInformation($"RunId: {runId} - Processing {testCases.Count} Symbol/Interval combinations.", runId, testCases.Count().ToString());

                var symbolKellyHalfFractions = new ConcurrentDictionary<string, decimal>();

                await Parallel.ForEachAsync(testCases, parallelOptions, async (testCase, cancellationToken) =>
                {
                    var symbol = testCase.Symbol;
                    var interval = testCase.Interval;

                    decimal currentSymbolKellyHalfFraction = symbolKellyHalfFractions.GetOrAdd(symbol + "_" + interval, 0.001m);

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
                            _logger.LogWarning("RunId: {RunId} - No historical data found for {Symbol} [{Interval}] *within* the requested date range {StartDate} to {EndDate}.", runId, symbol, interval, config.StartDate, config.EndDate);
                            return;
                        }
                        _logger.LogDebug("RunId: {RunId} - Bars within test range for {Symbol} [{Interval}]: {BarCount}", runId, symbol, interval, backtestData.Count);

                        Position? currentPosition = null;
                        TradeSummary? activeTrade = null; 

                        for (int i = 0; i < backtestData.Count; i++)
                        {
                            var currentBar = backtestData[i];

                            var dataWindow = orderedData.Where(x => x.Timestamp <= currentBar.Timestamp)
                                                       .OrderByDescending(x => x.Timestamp)
                                                       .Take(strategyMinimumLookback + rollingAdvLookbackBars + 5) 
                                                       .OrderBy(x => x.Timestamp)
                                                       .ToList();

                            if (dataWindow.Count < strategyMinimumLookback)
                            {
                                _logger.LogDebug("RunId: {RunId} - Insufficient data window for strategy indicators for {Symbol} [{Interval}] at {Timestamp}. Required: {Required}, Actual: {Actual}. Skipping bar.", runId, symbol, interval, strategyMinimumLookback, dataWindow.Count);
                                continue;
                            }

                            if (!_liquidityService.IsSymbolLiquidAtTime(symbol, interval, minimumAdv, currentBar.Timestamp, rollingAdvLookbackBars, orderedData))
                            {
                                _logger.LogDebug("RunId: {RunId} - Symbol {Symbol} [{Interval}] not liquid enough at {Timestamp}. Skipping entry consideration.", runId, symbol, interval, currentBar.Timestamp);
                                if (currentPosition == null)
                                    continue;
                            }

                            SignalDecision signal = strategyInstance.GenerateSignal(currentBar, dataWindow);

                            if (currentPosition != null && strategyInstance.ShouldExitPosition(currentPosition, currentBar, dataWindow))
                            {
                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Attempting to close position at Timestamp: {Timestamp}",
                                    runId, symbol, interval, currentBar.Timestamp);

                                decimal rawExitPrice = currentBar.ClosePrice;
                                PositionDirection exitPositionDirection = currentPosition.Direction;

                                decimal effectiveExitPrice = await _transactionCostService.CalculateExitCost(rawExitPrice, exitPositionDirection, symbol, interval, currentBar.Timestamp);
                                decimal exitSpreadCost = await _transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, currentBar.Timestamp);

                                decimal profitLossBeforeCosts;
                                if (currentPosition.Direction == PositionDirection.Long)
                                    profitLossBeforeCosts = (effectiveExitPrice - currentPosition.EntryPrice) * currentPosition.Quantity;
                                else
                                    profitLossBeforeCosts = (currentPosition.EntryPrice - effectiveExitPrice) * currentPosition.Quantity; 

                                decimal totalTradeTransactionCost = activeTrade.TransactionCost + exitSpreadCost;

                                decimal netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

                                await _portfolioManager.ClosePosition(
                                    strategyInstance.Name,
                                    symbol,
                                    interval,
                                    exitPositionDirection,
                                    currentPosition.Quantity,
                                    effectiveExitPrice,
                                    currentBar.Timestamp,
                                    totalTradeTransactionCost, 
                                    netProfitLoss              
                                );

                                var recentTradesForKelly = _portfolioManager.GetCompletedTradesHistory()
                                                                            .Where(t => t.Symbol == symbol && t.Interval == interval)
                                                                            .OrderByDescending(t => t.ExitDate)
                                                                            .Take(rollingKellyLookbackTrades)
                                                                            .ToList();

                                KellyMetrics kellyMetrics = _performanceCalculator.CalculateKellyMetrics(recentTradesForKelly);
                                currentSymbolKellyHalfFraction = kellyMetrics.KellyHalfFraction;
                                symbolKellyHalfFractions[symbol + "_" + interval] = currentSymbolKellyHalfFraction;

                                _logger.LogDebug("RunId: {RunId} - Symbol {Symbol} [{Interval}] - Kelly/2 updated to {KellyHalf:P2} (WinRate: {WinRate:P2}, Payoff: {Payoff:N2}) after trade closure. (Trades: {TradeCount})",
                                    runId, symbol, interval, currentSymbolKellyHalfFraction, kellyMetrics.WinRate, kellyMetrics.PayoffRatio, kellyMetrics.TotalTrades);

                                var finalizedClosedTrade = _portfolioManager.GetCompletedTradesHistory()
                                                                            .LastOrDefault(t => t.Symbol == symbol && t.EntryDate == activeTrade.EntryDate && t.ExitDate == currentBar.Timestamp);

                                if (finalizedClosedTrade != null)
                                {
                                    allTrades.Add(finalizedClosedTrade);
                                    await _tradesService.SaveBacktestResults(runId, new BacktestResult { Trades = new List<TradeSummary> { finalizedClosedTrade } }, symbol, interval);
                                    _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Position Closed. Timestamp: {Timestamp}. Net PnL: {NetPnL:C}", runId, symbol, interval, currentBar.Timestamp, netProfitLoss);
                                }
                                else
                                {
                                    _logger.LogWarning("RunId: {RunId} - Could not find just-closed trade for {Symbol} [{Interval}] in PortfolioManager history to save after closure at {Timestamp}. This may indicate a data discrepancy.", runId, symbol, interval, currentBar.Timestamp);
                                }

                                currentPosition = null;
                                activeTrade = null;
                            }

                            if (currentPosition == null && signal != SignalDecision.Hold)
                            {
                                decimal maxTradeAllocationInitialCapital = config.InitialCapital * 0.02m;
                                decimal currentTotalEquity = _portfolioManager.GetTotalEquity(); 

                                decimal actualAllocationAmount = strategyInstance.GetAllocationAmount(currentBar, dataWindow, maxTradeAllocationInitialCapital, currentTotalEquity, currentSymbolKellyHalfFraction);

                                if (actualAllocationAmount <= 0)
                                {
                                    _logger.LogWarning("RunId: {RunId} - Invalid allocation amount ({Allocation:C}) for {Symbol} [{Interval}] at {Timestamp}. Skipping entry.", runId, symbol, interval, actualAllocationAmount, currentBar.Timestamp);
                                    continue;
                                }

                                decimal effectiveEntryPrice = await _transactionCostService.CalculateEntryCost(currentBar.ClosePrice, signal, symbol, interval, currentBar.Timestamp);
                                if (effectiveEntryPrice <= 0)
                                {
                                    _logger.LogWarning("RunId: {RunId} - Invalid effective entry price ({EffectivePrice:C}) for {Symbol} [{Interval}] at {Timestamp}. Skipping entry.", runId, symbol, interval, effectiveEntryPrice, currentBar.Timestamp);
                                    continue;
                                }

                                int calculatedQuantity = (int)Math.Round(actualAllocationAmount / effectiveEntryPrice);
                                if (calculatedQuantity <= 0)
                                {
                                    _logger.LogWarning("RunId: {RunId} - Calculated quantity too small ({Quantity}) for {Symbol} [{Interval}] at {Timestamp}. Skipping entry.", runId, symbol, interval, calculatedQuantity, currentBar.Timestamp);
                                    continue;
                                }

                                decimal entrySpreadCost = await _transactionCostService.GetSpreadCost(currentBar.ClosePrice, calculatedQuantity, symbol, interval, currentBar.Timestamp);

                                decimal totalCostToOpen = (calculatedQuantity * effectiveEntryPrice) + entrySpreadCost;
                                if (!await _portfolioManager.CanOpenPosition(totalCostToOpen))
                                {
                                    _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Cannot open position for {Quantity} shares due to insufficient available cash after sizing. Skipping entry.", runId, symbol, interval, calculatedQuantity);
                                    continue;
                                }

                                await _portfolioManager.OpenPosition(symbol, interval, (signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short),
                                                                     calculatedQuantity, effectiveEntryPrice, currentBar.Timestamp, entrySpreadCost);

                                activeTrade = new TradeSummary
                                {
                                    EntryDate = currentBar.Timestamp,
                                    EntryPrice = effectiveEntryPrice,
                                    Direction = signal == SignalDecision.Buy ? "Long" : "Short",
                                    Quantity = calculatedQuantity,
                                    Symbol = symbol,  
                                    Interval = interval,
                                    TransactionCost = entrySpreadCost
                                };

                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Timestamp: {Timestamp}, Signal: {Signal}, Position Opened. Direction: {Direction}, Quantity: {Quantity}",
                                    runId, symbol, interval, currentBar.Timestamp, signal, activeTrade.Direction, activeTrade.Quantity);
                            }
                        }

                        if (currentPosition != null && activeTrade != null)
                        {
                            var lastBar = backtestData.Last();
                            decimal rawExitPrice = lastBar.ClosePrice;
                            PositionDirection exitPositionDirection = currentPosition.Direction;

                            decimal effectiveExitPrice = await _transactionCostService.CalculateExitCost(rawExitPrice, exitPositionDirection, symbol, interval, lastBar.Timestamp);
                            decimal exitSpreadCost = await _transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, lastBar.Timestamp);

                            decimal profitLossBeforeCosts;
                            if (currentPosition.Direction == PositionDirection.Long)
                                profitLossBeforeCosts = (effectiveExitPrice - currentPosition.EntryPrice) * currentPosition.Quantity;
                            else
                                profitLossBeforeCosts = (currentPosition.EntryPrice - effectiveExitPrice) * currentPosition.Quantity;

                            decimal totalTradeTransactionCost = activeTrade.TransactionCost + exitSpreadCost;
                            decimal netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

                            await _portfolioManager.ClosePosition(strategyInstance.Name, symbol, interval, exitPositionDirection, currentPosition.Quantity, effectiveExitPrice, lastBar.Timestamp, totalTradeTransactionCost, netProfitLoss);

                            var recentTradesForKelly = _portfolioManager.GetCompletedTradesHistory()
                                                                        .Where(t => t.Symbol == symbol && t.Interval == interval)
                                                                        .OrderByDescending(t => t.ExitDate)
                                                                        .Take(rollingKellyLookbackTrades)
                                                                        .ToList();

                            KellyMetrics kellyMetrics = _performanceCalculator.CalculateKellyMetrics(recentTradesForKelly);
                            currentSymbolKellyHalfFraction = kellyMetrics.KellyHalfFraction;
                            symbolKellyHalfFractions[symbol + "_" + interval] = currentSymbolKellyHalfFraction;

                            _logger.LogDebug("RunId: {RunId} - Symbol {Symbol} [{Interval}] - Kelly/2 updated to {KellyHalf:P2} (WinRate: {WinRate:P2}, Payoff: {Payoff:N2}) after final trade closure. (Trades: {TradeCount})",
                                runId, symbol, interval, currentSymbolKellyHalfFraction, kellyMetrics.WinRate, kellyMetrics.PayoffRatio, kellyMetrics.TotalTrades);

                            var finalClosedTrade = _portfolioManager.GetCompletedTradesHistory()
                                                                    .LastOrDefault(t => t.Symbol == symbol && t.EntryDate == activeTrade.EntryDate && t.ExitDate == lastBar.Timestamp);
                            if (finalClosedTrade != null)
                            {
                                allTrades.Add(finalClosedTrade);
                                await _tradesService.SaveBacktestResults(runId, new BacktestResult { Trades = new List<TradeSummary> { finalClosedTrade } }, symbol, interval);
                                _logger.LogInformation($"RunId: {runId}, Symbol: {symbol}, Interval: {interval}, Final Position Closed at end of backtest. Timestamp: {lastBar.Timestamp}. Net PnL: {netProfitLoss:C}", runId, symbol, interval, lastBar.Timestamp, netProfitLoss);
                            }
                            else
                            {
                                _logger.LogWarning("RunId: {RunId} - Could not find final closed trade for {Symbol} [{Interval}] in PortfolioManager history to save at end of backtest. This may indicate a data discrepancy.", runId, symbol, interval);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "RunId: {RunId} - Error processing {Symbol} [{Interval}]", runId, symbol, interval);
                    }
                });

                result.Trades.AddRange(_portfolioManager.GetCompletedTradesHistory());
                result.TotalTrades = result.Trades.Count;

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

        private int CalculatePositionSize(decimal currentBalance, decimal entryPrice, BacktestConfiguration config, ISingleAssetStrategy strategy)
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
