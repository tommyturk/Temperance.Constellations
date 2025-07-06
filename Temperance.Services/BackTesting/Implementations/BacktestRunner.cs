using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Implementations;
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;
using Temperance.Services.Trading.Strategies.Momentum;
using TradingApp.src.Core.Services.Interfaces;
namespace Temperance.Services.BackTesting.Implementations
{
    public class BacktestRunner : IBacktestRunner
    {
        private readonly IHistoricalPriceService _historicalPriceService;
        private readonly ILiquidityService _liquidityService;
        private readonly ITransactionCostService _transactionCostService;
        private readonly IGpuIndicatorService _gpuIndicatorService;
        private readonly IPortfolioManager _portfolioManager;
        private readonly IStrategyFactory _strategyFactory;
        private readonly ITradeService _tradesService;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly IBacktestRepository _backtestRepository;
        private readonly ILogger<BacktestRunner> _logger;

        public BacktestRunner(
            IHistoricalPriceService historicalPriceService,
            ILiquidityService liquidityService,
            ITransactionCostService transactionCostService,
            IGpuIndicatorService gpuIndicatorService,
            IPortfolioManager portfolioManager,
            IStrategyFactory strategyFactory,
            ITradeService tradesService,
            ISecuritiesOverviewService securitiesOverviewService,
            IPerformanceCalculator performanceCalculator,
            IBacktestRepository backtestRepository,
            ILogger<BacktestRunner> logger)
        {
            _historicalPriceService = historicalPriceService;
            _liquidityService = liquidityService;
            _transactionCostService = transactionCostService;
            _gpuIndicatorService = gpuIndicatorService;
            _portfolioManager = portfolioManager;
            _strategyFactory = strategyFactory;
            _tradesService = tradesService;
            _securitiesOverviewService = securitiesOverviewService;
            _performanceCalculator = performanceCalculator;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task RunBacktest(string configJson, Guid runId)
        {
            var config = JsonSerializer.Deserialize<BacktestConfiguration>(configJson);
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
                var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(
                    config.StrategyName,
                    config.InitialCapital,
                    config.StrategyParameters
                );

                if (strategyInstance == null)
                    throw new InvalidOperationException($"Strategy '{config.StrategyName}' could not be created.");

                strategyInstance.Initialize(config.InitialCapital, config.StrategyParameters);

                int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
                _logger.LogInformation("RunId: {RunId} - Strategy '{StrategyName}' requires minimum lookback of {MinLookback} bars.", runId, config.StrategyName, strategyMinimumLookback);

                long minimumAdv = strategyInstance.GetMinimumAverageDailyVolume();
                int rollingAdvLookbackBars = 20;
                int rollingKellyLookbackTrades = 50;

                var symbolsWithCoverage = await _securitiesOverviewService.GetSecuritiesForBacktest(config.Symbols);

                if (!symbolsWithCoverage.Any()) throw new InvalidOperationException("No symbols specified or found for backtest.");

                var indicatorCache = new ConcurrentDictionary<string, Dictionary<string, double[]>>();

                foreach (var testCase in symbolsWithCoverage)
                {
                    testCase.Symbol = testCase.Symbol.Trim();
                    testCase.Interval = testCase.Interval.Trim();
                    string cacheKey = $"{testCase.Symbol}_{testCase.Interval}";
                    var historicalData = await _historicalPriceService.GetHistoricalPrices(testCase.Symbol, testCase.Interval);
                    if (historicalData == null || !historicalData.Any())
                    {
                        _logger.LogWarning("RunId: {RunId} - No historical data found for {Symbol} [{Interval}]", runId, testCase.Symbol, testCase.Interval);
                        continue;
                    }
                    var closePrices = historicalData.OrderBy(p => p.Timestamp).Select(p => (double)p.ClosePrice)
                        .ToArray();

                    var movingAverage = _gpuIndicatorService.CalculateSma(closePrices, strategyMinimumLookback);
                    var standardDeviation = _gpuIndicatorService.CalculateStdDev(closePrices, strategyMinimumLookback);
                    var rsi = strategyInstance.CalculateRSI(closePrices, strategyMinimumLookback);
                    var upperBand = new double[closePrices.Length];
                    var lowerBand = new double[closePrices.Length];
                    for (int i = 0; i < closePrices.Length; i++)
                    {
                        upperBand[i] = (movingAverage[i] + (2 * standardDeviation[i]));
                        lowerBand[i] = (movingAverage[i] - (2 * standardDeviation[i]));
                    }

                    indicatorCache[cacheKey] = new Dictionary<string, double[]>
                    {
                        { "RSI", rsi },
                        { "UpperBand", upperBand },
                        { "LowerBand", lowerBand }
                    };
                }

                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };

                _logger.LogInformation($"RunId: {runId} - Processing {symbolsWithCoverage.Count} Symbol/Interval combinations.", runId, symbolsWithCoverage.Count().ToString());
                var symbolKellyHalfFractions = new ConcurrentDictionary<string, double>();

                await Parallel.ForEachAsync(symbolsWithCoverage, parallelOptions, async (testCase, cancellationToken) =>
                {
                    var symbol = testCase.Symbol;
                    var interval = testCase.Interval;
                    string cacheKey = $"{symbol}_{interval}";

                    if (!indicatorCache.ContainsKey(cacheKey)) { return; }

                    double currentSymbolKellyHalfFraction = symbolKellyHalfFractions.GetOrAdd(symbol + "_" + interval, 0.001);

                    try
                    {
                        var orderedData = (await _historicalPriceService.GetHistoricalPrices(symbol, interval)).OrderBy(x => x.Timestamp).ToList();
                        var backtestData = orderedData.Where(x => x.Timestamp >= config.StartDate && x.Timestamp <= config.EndDate).ToList();

                        if (!backtestData.Any())
                        {
                            _logger.LogWarning("RunId: {RunId} - No data for {Symbol} in date range. This should have been caught earlier.", runId, symbol);
                            return;
                        }

                        var timestampIndexMap = orderedData.Select((data, index) => new { data.Timestamp, index })
                                                           .ToDictionary(x => x.Timestamp, x => x.index);

                        Position? currentPosition = null;
                        TradeSummary? activeTrade = null;
                        List<TradeSummary> tradesForThisCase = new List<TradeSummary>();

                        for (int i = 0; i < backtestData.Count; i++)
                        {
                            var currentBar = backtestData[i];

                            if (!timestampIndexMap.TryGetValue(currentBar.Timestamp, out var globalIndex) || globalIndex < strategyMinimumLookback)
                                continue;

                            var currentIndicatorValues = new Dictionary<string, double>
                            {
                                { "RSI", indicatorCache[cacheKey]["RSI"][globalIndex] },
                                { "UpperBand", indicatorCache[cacheKey]["UpperBand"][globalIndex] },
                                { "LowerBand", indicatorCache[cacheKey]["LowerBand"][globalIndex] }
                            };

                            var dataWindow = orderedData.Where(x => x.Timestamp <= currentBar.Timestamp)
                                                       .OrderByDescending(x => x.Timestamp)
                                                       .Take(strategyMinimumLookback + rollingAdvLookbackBars + 5)
                                                       .OrderBy(x => x.Timestamp)
                                                       .ToList();

                            if (!_liquidityService.IsSymbolLiquidAtTime(symbol, interval, minimumAdv, currentBar.Timestamp, rollingAdvLookbackBars, orderedData))
                            {
                                _logger.LogDebug("RunId: {RunId} - Symbol {Symbol} [{Interval}] not liquid enough at {Timestamp}. Skipping entry consideration.", runId, symbol, interval, currentBar.Timestamp);
                                if (currentPosition == null)
                                    continue;
                            }

                            SignalDecision signal = strategyInstance.GenerateSignal(currentBar, dataWindow, currentIndicatorValues);

                            if (currentPosition != null && strategyInstance.ShouldExitPosition(currentPosition, currentBar, dataWindow, currentIndicatorValues))
                            {
                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Attempting to close position at Timestamp: {Timestamp}",
                                    runId, symbol, interval, currentBar.Timestamp);

                                double rawExitPrice = currentBar.ClosePrice;
                                PositionDirection exitPositionDirection = currentPosition.Direction;

                                double effectiveExitPrice = await _transactionCostService.CalculateExitCost(rawExitPrice, exitPositionDirection, symbol, interval, currentBar.Timestamp);
                                double exitSpreadCost = await _transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, currentBar.Timestamp);

                                double profitLossBeforeCosts;
                                if (currentPosition.Direction == PositionDirection.Long)
                                    profitLossBeforeCosts = (effectiveExitPrice - currentPosition.EntryPrice) * currentPosition.Quantity;
                                else
                                    profitLossBeforeCosts = (currentPosition.EntryPrice - effectiveExitPrice) * currentPosition.Quantity;

                                double totalTradeTransactionCost = activeTrade.TransactionCost + exitSpreadCost;

                                double netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

                                TradeSummary? closedTrade = await _portfolioManager.ClosePosition(
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

                                if (closedTrade != null)
                                    tradesForThisCase.Add(closedTrade);

                                _logger.LogWarning("RunId: {RunId} - Could not close position for {Symbol} [{Interval}] at {Timestamp}. This may indicate a data discrepancy.", runId, symbol, interval, currentBar.Timestamp);

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
                                double maxTradeAllocationInitialCapital = config.InitialCapital;
                                double currentTotalEquity = _portfolioManager.GetTotalEquity();

                                double actualAllocationAmount = strategyInstance.GetAllocationAmount(currentBar, dataWindow, maxTradeAllocationInitialCapital, currentTotalEquity, currentSymbolKellyHalfFraction);

                                if (actualAllocationAmount <= 0)
                                {
                                    _logger.LogWarning("RunId: {RunId} - Invalid allocation amount ({Allocation:C}) for {Symbol} [{Interval}] at {Timestamp}. Skipping entry.", runId, symbol, interval, actualAllocationAmount, currentBar.Timestamp);
                                    continue;
                                }

                                double effectiveEntryPrice = await _transactionCostService.CalculateEntryCost(currentBar.ClosePrice, signal, symbol, interval, currentBar.Timestamp);
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

                                double entrySpreadCost = await _transactionCostService.GetSpreadCost(currentBar.ClosePrice, calculatedQuantity, symbol, interval, currentBar.Timestamp);

                                double totalCostToOpen = (calculatedQuantity * effectiveEntryPrice) + entrySpreadCost;
                                if (!await _portfolioManager.CanOpenPosition(totalCostToOpen))
                                {
                                    _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Cannot open position for {Quantity} shares due to insufficient available cash after sizing. Skipping entry.", runId, symbol, interval, calculatedQuantity);
                                    continue;
                                }

                                await _portfolioManager.OpenPosition(symbol, interval, (signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short),
                                                                     calculatedQuantity, effectiveEntryPrice, currentBar.Timestamp, entrySpreadCost);

                                IEnumerable<TradeSummary> trades = new List<TradeSummary>()
                                {
                                    new TradeSummary(){
                                        RunId = runId,
                                        StrategyName = strategyInstance.Name,
                                        EntryDate = currentBar.Timestamp,
                                        EntryPrice = effectiveEntryPrice,
                                        Direction = signal == SignalDecision.Buy ? "Long" : "Short",
                                        Quantity = calculatedQuantity,
                                        Symbol = symbol,
                                        Interval = interval,
                                        TransactionCost = entrySpreadCost,
                                    }
                                };

                                currentPosition = _portfolioManager.GetOpenPositions().FirstOrDefault(p => p.Symbol == symbol);

                                if (tradesForThisCase.Any())
                                {
                                    tradesForThisCase.ForEach(t => t.RunId = runId);
                                    await _backtestRepository.SaveBacktestTradesAsync(runId, allTrades);

                                    foreach (var trade in tradesForThisCase)
                                    {
                                        allTrades.Add(trade);
                                    }
                                }
                                _logger.LogInformation("RunId: {RunId}, Symbol: {Symbol}, Interval: {Interval}, Timestamp: {Timestamp}, Signal: {Signal}, Position Opened. Direction: {Direction}, Quantity: {Quantity}",
                                    runId, symbol, interval, currentBar.Timestamp, signal, activeTrade.Direction, activeTrade.Quantity);
                            }
                        }

                        if (currentPosition != null && activeTrade != null)
                        {
                            var lastBar = backtestData.Last();
                            double rawExitPrice = lastBar.ClosePrice;
                            PositionDirection exitPositionDirection = currentPosition.Direction;

                            double effectiveExitPrice = await _transactionCostService.CalculateExitCost(rawExitPrice, exitPositionDirection, symbol, interval, lastBar.Timestamp);
                            double exitSpreadCost = await _transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, lastBar.Timestamp);

                            double profitLossBeforeCosts;
                            if (currentPosition.Direction == PositionDirection.Long)
                                profitLossBeforeCosts = (effectiveExitPrice - currentPosition.EntryPrice) * currentPosition.Quantity;
                            else
                                profitLossBeforeCosts = (currentPosition.EntryPrice - effectiveExitPrice) * currentPosition.Quantity;

                            double totalTradeTransactionCost = activeTrade.TransactionCost + exitSpreadCost;
                            double netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

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


        [AutomaticRetry(Attempts = 1)]
        public async Task RunDualMomentumBacktest(string configJson, Guid runId)
        {
            var config = JsonSerializer.Deserialize<DualMomentumBacktestConfiguration>(configJson);
            if (config == null || !config.RiskAssetSymbols.Any() || string.IsNullOrWhiteSpace(config.SafeAssetSymbol))
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Invalid configuration for Dual Momentum Backtest.");
                throw new ArgumentException("Invalid configuration for Dual Momentum Backtest.");
            }

            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");
            _logger.LogInformation("Starting Dual Momentum backtest for RunId: {RunId}", runId);

            await _portfolioManager.Initialize(config.InitialCapital);
            var allTrades = new ConcurrentBag<TradeSummary>();
            var riskAssetKellyHalfFractions = new ConcurrentDictionary<string, double>();

            string strategyParametersJson = JsonSerializer.Serialize(config.StrategyParameters);
            var strategyInstance = _strategyFactory.CreateStrategy<IDualMomentumStrategy>(
                config.StrategyName, config.InitialCapital, config.StrategyParameters);

            if (strategyInstance == null)
                throw new InvalidOperationException($"Could not create a valid IDualMomentumStrategy for '{config.StrategyName}'.");

            int lookbackPeriod = config.MomentumLookbackMonths;
            var testCases = config.RiskAssetSymbols.Select(symbol => new { Symbol = symbol }).ToList();

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };
            await Parallel.ForEachAsync(testCases, parallelOptions, async (testCase, cancellationToken) =>
            {
                var dmStrategy = strategyInstance as DualMomentumStrategy;
                if (dmStrategy == null)
                {
                    _logger.LogError("RunId: {RunId} - Strategy instance is not a valid DualMomentumStrategy.", runId);
                    return;
                }

                var assetDataCache = new Dictionary<string, List<HistoricalPriceModel>>();
                var allPortfolioAssets = new List<string>(config.RiskAssetSymbols) { config.SafeAssetSymbol };
                foreach (var symbol in allPortfolioAssets)
                {
                    var data = await _historicalPriceService.GetHistoricalPrices(symbol, string.Empty);
                    assetDataCache[symbol] = data.OrderBy(d => d.Timestamp).ToList();
                }
            });
        }


        public async Task RunPairsBacktest(PairsBacktestConfiguration config, Guid runId)
        {
            var result = new BacktestResult();
            // --- Initial Setup ---
            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");
            _logger.LogInformation("Starting GPU-accelerated pairs backtest for RunId: {RunId}", runId);

            await _portfolioManager.Initialize(config.InitialCapital);
            var allTrades = new ConcurrentBag<TradeSummary>();
            var pairKellyHalfFractions = new ConcurrentDictionary<string, double>();

            string strategyParametersJson = JsonSerializer.Serialize(config.StrategyParameters);

            Dictionary<string, object> strategyParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(strategyParametersJson)
                ?? new Dictionary<string, object>();

            var strategyInstance = _strategyFactory.CreateStrategy<IPairTradingStrategy>(
                config.StrategyName, config.InitialCapital, strategyParameters);

            if (strategyInstance == null)
                throw new InvalidOperationException($"Could not create a valid IPairTradingStrategy for '{config.StrategyName}'.");

            int lookbackPeriod = strategyInstance.GetRequiredLookbackPeriod();
            int rollingKellyLookbackTrades = 50;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };

            //await Parallel.ForEachAsync(config.PairsToTest, parallelOptions, async (pair, cancellationToken) =>
            //{
            foreach (var pair in config.PairsToTest)
            {
                var pairIdentifier = $"{pair.SymbolA}/{pair.SymbolB}";
                try
                {
                    // --- Stage 1: Data Fetching, Alignment, and Full Indicator Pre-calculation ---
                    var historicalDataA = await _historicalPriceService.GetHistoricalPrices(pair.SymbolA, config.Interval);
                    var historicalDataB = await _historicalPriceService.GetHistoricalPrices(pair.SymbolB, config.Interval);

                    var alignedData = AlignData(historicalDataA, historicalDataB);
                    if (alignedData.Count < lookbackPeriod)
                    {
                        _logger.LogWarning("Insufficient aligned data for pair {Pair} to meet lookback of {Lookback}", pairIdentifier, lookbackPeriod);
                        return;
                    }

                    // Extract aligned close prices into arrays for GPU processing
                    var closePricesA = alignedData.Select(d => (double)d.Item1.ClosePrice).ToArray();
                    var closePricesB = alignedData.Select(d => (double)d.Item2.ClosePrice).ToArray();

                    // 1. Calculate the entire spread series
                    var spreadSeries = new double[alignedData.Count];
                    for (int i = 0; i < alignedData.Count; i++)
                        spreadSeries[i] = closePricesA[i] - (pair.HedgeRatio * closePricesB[i]);

                    var spreadSma = _gpuIndicatorService.CalculateSma(spreadSeries, lookbackPeriod);
                    var spreadStdDev = _gpuIndicatorService.CalculateStdDev(spreadSeries, lookbackPeriod);

                    var zScoreSeries = new double[alignedData.Count];
                    for (int i = 0; i < alignedData.Count; i++)
                    {
                        if (spreadStdDev[i] != 0)
                            zScoreSeries[i] = (spreadSeries[i] - spreadSma[i]) / spreadStdDev[i];
                        else
                            zScoreSeries[i] = 0;
                    }

                    var backtestData = alignedData
                        .Where(d => d.Item1.Timestamp >= config.StartDate && d.Item1.Timestamp <= config.EndDate).ToList();

                    var timestampIndexMap = alignedData.Select((data, index) => new { data.Item1.Timestamp, index })
                                                     .ToDictionary(x => x.Timestamp, x => x.index);

                    ActivePairTrade? activePairTrade = null;
                    double currentPairKellyHalfFraction = pairKellyHalfFractions.GetOrAdd(pairIdentifier, 0.01);

                    foreach (var (currentBarA, currentBarB) in backtestData)
                    {
                        if (!timestampIndexMap.TryGetValue(currentBarA.Timestamp, out var globalIndex) || globalIndex < lookbackPeriod)
                            continue;

                        var currentIndicatorValues = new Dictionary<string, double>
                        {
                            { "ZScore", zScoreSeries[globalIndex] }
                        };

                        // --- Decision Making ---
                        var signal = strategyInstance.GenerateSignal(currentBarA, currentBarB, currentIndicatorValues);

                        if (activePairTrade != null && strategyInstance.ShouldExitPosition(new Position { Direction = activePairTrade.Direction, EntryDate = activePairTrade.EntryDate }, currentBarA, currentBarB, currentIndicatorValues))
                        {
                            _logger.LogInformation("RunId: {RunId}, Pair: {Pair}, Attempting to close position at Timestamp: {Timestamp}", runId, pairIdentifier, currentBarA.Timestamp);

                            var rawExitPriceA = currentBarA.ClosePrice;
                            var directionA = activePairTrade.Direction == PositionDirection.Long ? PositionDirection.Long : PositionDirection.Short;
                            var effectiveExitPriceA = await _transactionCostService.CalculateExitCost(rawExitPriceA, directionA, pair.SymbolA, config.Interval, currentBarA.Timestamp);
                            var exitSpreadCostA = await _transactionCostService.GetSpreadCost(rawExitPriceA, (int)activePairTrade.QuantityA, pair.SymbolA, config.Interval, currentBarA.Timestamp);

                            var rawExitPriceB = currentBarB.ClosePrice;
                            var directionB = activePairTrade.Direction == PositionDirection.Long ? PositionDirection.Short : PositionDirection.Long;
                            var effectiveExitPriceB = await _transactionCostService.CalculateExitCost(rawExitPriceB, directionB, pair.SymbolB, config.Interval, currentBarB.Timestamp);
                            var exitSpreadCostB = await _transactionCostService.GetSpreadCost(rawExitPriceB, (int)activePairTrade.QuantityB, pair.SymbolB, config.Interval, currentBarB.Timestamp);

                            var pnlA = (directionA == PositionDirection.Long) ? (effectiveExitPriceA - activePairTrade.EntryPriceA) * activePairTrade.QuantityA : (activePairTrade.EntryPriceA - effectiveExitPriceA) * activePairTrade.QuantityA;
                            var pnlB = (directionB == PositionDirection.Long) ? (effectiveExitPriceB - activePairTrade.EntryPriceB) * activePairTrade.QuantityB : (activePairTrade.EntryPriceB - effectiveExitPriceB) * activePairTrade.QuantityB;

                            var profitLossBeforeCosts = pnlA + pnlB;
                            var totalExitTransactionCost = exitSpreadCostA + exitSpreadCostB;
                            var totalTradeTransactionCost = activePairTrade.TotalEntryTransactionCost + totalExitTransactionCost;
                            var netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

                            var finalizedClosedTrade = await _portfolioManager.ClosePairPosition(activePairTrade, effectiveExitPriceA, effectiveExitPriceB, currentBarA.Timestamp, totalTradeTransactionCost);

                            var recentTradesForKelly = _portfolioManager.GetCompletedTradesHistory().Where(t => t.Symbol == pairIdentifier && t.Interval == config.Interval).OrderByDescending(t => t.ExitDate).Take(rollingKellyLookbackTrades).ToList();
                            KellyMetrics kellyMetrics = _performanceCalculator.CalculateKellyMetrics(recentTradesForKelly);
                            currentPairKellyHalfFraction = kellyMetrics.KellyHalfFraction;
                            pairKellyHalfFractions[pairIdentifier] = currentPairKellyHalfFraction;

                            if (finalizedClosedTrade != null)
                            {
                                allTrades.Add(finalizedClosedTrade);
                                _logger.LogInformation("RunId: {RunId}, Pair: {Pair}, Position Closed. PnL: {PnL:C}", runId, pairIdentifier, netProfitLoss);
                            }
                            activePairTrade = null;
                        }

                        // --- Open Position Logic ---
                        if (activePairTrade != null && signal != SignalDecision.Hold)
                        {
                            // This block for opening a position (sizing, calculating costs, updating portfolio)
                            // also remains the same.
                            #region Open Position Logic
                            double allocation = _portfolioManager.GetTotalEquity() * currentPairKellyHalfFraction;
                            if (allocation <= 0) continue;

                            long quantityA = (long)(allocation / currentBarA.ClosePrice);
                            long quantityB = (long)((quantityA * (long)currentBarA.ClosePrice * pair.HedgeRatio) / (long)currentBarB.ClosePrice);
                            if (quantityA <= 0 || quantityB <= 0) continue;

                            var directionA = signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short;
                            var effectiveEntryPriceA = await _transactionCostService.CalculateEntryCost(currentBarA.ClosePrice, signal, pair.SymbolA, config.Interval, currentBarA.Timestamp);
                            var entrySpreadCostA = await _transactionCostService.GetSpreadCost(currentBarA.ClosePrice, quantityA, pair.SymbolA, config.Interval, currentBarA.Timestamp);

                            var directionB = signal == SignalDecision.Buy ? PositionDirection.Short : PositionDirection.Long;
                            var effectiveEntryPriceB = await _transactionCostService.CalculateEntryCost(currentBarB.ClosePrice, signal, pair.SymbolB, config.Interval, currentBarB.Timestamp);
                            var entrySpreadCostB = await _transactionCostService.GetSpreadCost(currentBarB.ClosePrice, quantityB, pair.SymbolB, config.Interval, currentBarB.Timestamp);

                            double totalCostToOpen = (quantityA * effectiveEntryPriceA) + (quantityB * effectiveEntryPriceB) + entrySpreadCostA + entrySpreadCostB;
                            if (!await _portfolioManager.CanOpenPosition(totalCostToOpen)) continue;

                            activePairTrade = new ActivePairTrade(pair.SymbolA, pair.SymbolB, (long)pair.HedgeRatio, (signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short), quantityA, quantityB, effectiveEntryPriceA, effectiveEntryPriceB, currentBarA.Timestamp, entrySpreadCostA + entrySpreadCostB);
                            await _portfolioManager.OpenPairPosition(strategyInstance.Name, pairIdentifier, config.Interval, activePairTrade);
                            _logger.LogInformation("RunId: {RunId}, Pair: {Pair}, Position Opened. Direction: {Direction}", runId, pairIdentifier, activePairTrade.Direction);
                            #endregion
                        }
                    }
                    // Logic to close any trade still open at the end of the test period would go here.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RunId: {RunId} - Unhandled error processing pair {Pair}", runId, pairIdentifier);
                }
                //});
            }

            if (allTrades.Any())
            {
                foreach (var trade in allTrades)
                {
                    trade.RunId = runId;
                }
                await _backtestRepository.SaveBacktestTradesAsync(runId, allTrades);
                _logger.LogInformation("RunId: {RunId} - Saved {TradeCount} trades for pairs backtest.", runId, allTrades.Count);
            }

            // --- Finalization ---
            result.Trades.AddRange(allTrades);
            result.TotalTrades = result.Trades.Count;
            await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
            await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");
        }

        private List<(HistoricalPriceModel, HistoricalPriceModel)> AlignData(
            IEnumerable<HistoricalPriceModel> dataA,
            IEnumerable<HistoricalPriceModel> dataB)
        {
            if (dataA == null || dataB == null)
                return new List<(HistoricalPriceModel, HistoricalPriceModel)>();

            var joinedQuery = dataA.Join(
                dataB,
                barA => barA.Timestamp,
                barB => barB.Timestamp,
                (barA, barB) => (barA, barB)
            );

            return joinedQuery.OrderBy(pair => pair.barA.Timestamp).ToList();
        }
    }
}
