using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;
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
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BacktestRunner> _logger;

        public BacktestRunner(
            ILiquidityService liquidityService,
            ITransactionCostService transactionCostService,
            IGpuIndicatorService gpuIndicatorService,
            IPortfolioManager portfolioManager,
            IStrategyFactory strategyFactory,
            ITradeService tradesService,
            ISecuritiesOverviewService securitiesOverviewService,
            IPerformanceCalculator performanceCalculator,
            IBacktestRepository backtestRepository,
            IServiceProvider serviceProvider,
            ILogger<BacktestRunner> logger)
        {
            _liquidityService = liquidityService;
            _transactionCostService = transactionCostService;
            _gpuIndicatorService = gpuIndicatorService;
            _portfolioManager = portfolioManager;
            _strategyFactory = strategyFactory;
            _tradesService = tradesService;
            _securitiesOverviewService = securitiesOverviewService;
            _performanceCalculator = performanceCalculator;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task RunBacktest(string configJson, Guid runId)
        {
            var config = JsonSerializer.Deserialize<BacktestConfiguration>(configJson);
            if (config == null)
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Configuration error.");
                throw new ArgumentException("Could not deserialize configuration.", nameof(configJson));
            }

            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");

            var result = new BacktestResult();
            try
            {
                var testCaseStream = _securitiesOverviewService.StreamSecuritiesForBacktest(config.Symbols, config.Intervals);
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };
                var allTrades = new ConcurrentBag<TradeSummary>();
                var symbolKellyHalfFractions = new ConcurrentDictionary<string, double>();

                await Parallel.ForEachAsync(testCaseStream, parallelOptions, async (testCase, cancellationToken) =>
                {
                    await using var scope = _serviceProvider.CreateAsyncScope();
                    var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(config.StrategyName, config.InitialCapital, config.StrategyParameters);
                    if (strategyInstance == null) return;

                    var portfolioManager = scope.ServiceProvider.GetRequiredService<IPortfolioManager>();
                    var historicalPriceService = scope.ServiceProvider.GetRequiredService<IHistoricalPriceService>();
                    var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();
                    var liquidityService = scope.ServiceProvider.GetRequiredService<ILiquidityService>();
                    var performanceCalculator = scope.ServiceProvider.GetRequiredService<IPerformanceCalculator>();
                    var tradesService = scope.ServiceProvider.GetRequiredService<ITradeService>();

                    await portfolioManager.Initialize(config.InitialCapital);

                    var symbol = testCase.Symbol.Trim();
                    var interval = testCase.Interval.Trim();

                    try
                    {
                        var orderedData = (await historicalPriceService.GetHistoricalPrices(symbol, interval)).OrderBy(d => d.Timestamp).ToList();

                        int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
                        if (orderedData.Count < strategyMinimumLookback) return;

                        var lastBarTimestamps = new HashSet<DateTime>();
                        if (config.UseMocExit)
                        {
                            lastBarTimestamps = orderedData.GroupBy(p => p.Timestamp.Date)
                                                           .Select(g => g.Max(p => p.Timestamp))
                                                           .ToHashSet();
                        }

                        _logger.LogInformation($"RunId: {runId} - Processing {symbol} [{interval}]");

                        var closePrices = orderedData.Select(p => p.ClosePrice).ToArray();
                        var movingAverage = _gpuIndicatorService.CalculateSma(closePrices, strategyMinimumLookback);
                        var standardDeviation = _gpuIndicatorService.CalculateStdDev(closePrices, strategyMinimumLookback);
                        var rsi = strategyInstance.CalculateRSI(closePrices, strategyMinimumLookback);
                        var upperBand = movingAverage.Zip(standardDeviation, (m, s) => m + (2 * s)).ToArray();
                        var lowerBand = movingAverage.Zip(standardDeviation, (m, s) => m - (2 * s)).ToArray();
                        var indicators = new Dictionary<string, double[]>
                        {
                            { "RSI", rsi }, { "UpperBand", upperBand }, { "LowerBand", lowerBand }
                        };

                        var timestampIndexMap = orderedData.Select((data, index) => new { data.Timestamp, index }).ToDictionary(x => x.Timestamp, x => x.index);
                        int backtestStartIndex = orderedData.FindIndex(p => p.Timestamp >= config.StartDate);
                        if (backtestStartIndex == -1) return;

                        Position? currentPosition = null;
                        TradeSummary? activeTrade = null;
                        double currentSymbolKellyHalfFraction = symbolKellyHalfFractions.GetOrAdd($"{symbol}_{interval}", 0.01);

                        for (int i = backtestStartIndex; i < orderedData.Count; i++)
                        {
                            var currentBar = orderedData[i];
                            if (currentBar.Timestamp > config.EndDate) break;
                            if (!timestampIndexMap.TryGetValue(currentBar.Timestamp, out var globalIndex) || globalIndex < strategyMinimumLookback) continue;

                            var currentIndicatorValues = new Dictionary<string, double>
                            {
                                { "RSI", indicators["RSI"][globalIndex] },
                                { "UpperBand", indicators["UpperBand"][globalIndex] },
                                { "LowerBand", indicators["LowerBand"][globalIndex] }
                            };

                            ReadOnlySpan<HistoricalPriceModel> dataWindowSpan = CollectionsMarshal.AsSpan(orderedData).Slice(0, i + 1);
                            bool shouldExit = false;
                            var exitReason = "Hold";

                            if (currentPosition != null)
                            {
                                bool strategyExitTriggered = strategyInstance.ShouldExitPosition(currentPosition, in currentBar, dataWindowSpan, currentIndicatorValues);
                                if (config.UseMocExit)
                                {
                                    bool isMocBar = lastBarTimestamps.Contains(currentBar.Timestamp);
                                    if (strategyExitTriggered) { shouldExit = true; exitReason = "Strategy Exit (Stop-Loss)"; }
                                    else if (isMocBar) { shouldExit = true; exitReason = "Market on Close"; }
                                }
                                else if (strategyExitTriggered) { shouldExit = true; exitReason = "Strategy Exit"; }
                            }

                            if (shouldExit && currentPosition != null && activeTrade != null)
                            {
                                var closedTrade = await ClosePositionAsync(portfolioManager, transactionCostService, performanceCalculator, tradesService, strategyInstance, activeTrade, currentPosition, currentBar, orderedData, symbol, interval, runId, 50, symbolKellyHalfFractions);
                                if (closedTrade != null)
                                {
                                    closedTrade.ExitReason = exitReason;
                                    allTrades.Add(closedTrade);
                                }
                                currentPosition = null;
                                activeTrade = null;
                                continue;
                            }

                            var signal = strategyInstance.GenerateSignal(in currentBar, currentPosition, dataWindowSpan, currentIndicatorValues);
                            if (signal != SignalDecision.Hold)
                            {
                                if (currentPosition == null)
                                {
                                    long minimumAdv = strategyInstance.GetMinimumAverageDailyVolume();
                                    if (!liquidityService.IsSymbolLiquidAtTime(symbol, interval, minimumAdv, currentBar.Timestamp, 20, orderedData)) continue;

                                    double allocationAmount = strategyInstance.GetAllocationAmount(in currentBar, dataWindowSpan, currentIndicatorValues, config.InitialCapital * 0.02, portfolioManager.GetTotalEquity(), currentSymbolKellyHalfFraction, 1);
                                    if (allocationAmount > 0)
                                    {
                                        double rawEntryPrice = currentBar.ClosePrice;
                                        int quantity = (int)Math.Round(allocationAmount / rawEntryPrice);
                                        if (quantity <= 0) continue;

                                        var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;
                                        double effectiveEntryPrice = await transactionCostService.CalculateEntryCost(rawEntryPrice, signal, symbol, interval, currentBar.Timestamp);
                                        double commissionCost = await transactionCostService.CalculateCommissionCost(rawEntryPrice, quantity, symbol, interval, currentBar.Timestamp);
                                        double slippageCost = await transactionCostService.CalculateSlippageCost(rawEntryPrice, quantity, direction, symbol, interval, currentBar.Timestamp);
                                        double spreadAndOtherCost = await transactionCostService.GetSpreadCost(rawEntryPrice, quantity, symbol, interval, currentBar.Timestamp);
                                        double totalEntryCost = commissionCost + slippageCost + spreadAndOtherCost;
                                        double totalCashOutlay = (quantity * effectiveEntryPrice) + totalEntryCost;

                                        if (await portfolioManager.CanOpenPosition(totalCashOutlay))
                                        {
                                            await portfolioManager.OpenPosition(symbol, interval, direction, quantity, effectiveEntryPrice, currentBar.Timestamp, totalEntryCost);
                                            activeTrade = new TradeSummary
                                            {
                                                Id = Guid.NewGuid(),
                                                RunId = runId,
                                                StrategyName = strategyInstance.Name,
                                                EntryDate = currentBar.Timestamp,
                                                EntryPrice = effectiveEntryPrice,
                                                Direction = direction.ToString(),
                                                Quantity = quantity,
                                                Symbol = symbol,
                                                Interval = interval,
                                                CommissionCost = commissionCost,
                                                SlippageCost = slippageCost,
                                                OtherTransactionCost = spreadAndOtherCost,
                                                TotalTransactionCost = totalEntryCost,
                                                EntryReason = strategyInstance.GetEntryReason(in currentBar, orderedData.Take(i + 1).ToList(), currentIndicatorValues)
                                            };
                                            currentPosition = portfolioManager.GetOpenPositions().FirstOrDefault(p => p.Symbol == symbol);
                                            await tradesService.SaveOrUpdateBacktestTrade(activeTrade);
                                        }
                                    }
                                }
                                else
                                {
                                    var expectedDirection = signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short;
                                    if (expectedDirection == currentPosition.Direction && currentPosition.PyramidEntries < strategyInstance.GetMaxPyramidEntries())
                                    {
                                        double allocationAmount = strategyInstance.GetAllocationAmount(in currentBar, dataWindowSpan, currentIndicatorValues, config.InitialCapital * 0.02, portfolioManager.GetTotalEquity(), currentSymbolKellyHalfFraction, currentPosition.PyramidEntries + 1);
                                        if (allocationAmount > 0)
                                        {
                                            double rawEntryPrice = currentBar.ClosePrice;
                                            int quantityToAdd = (int)Math.Round(allocationAmount / rawEntryPrice);
                                            if (quantityToAdd <= 0) continue;

                                            double effectiveEntryPrice = await transactionCostService.CalculateEntryCost(rawEntryPrice, signal, symbol, interval, currentBar.Timestamp);
                                            double commissionCost = await transactionCostService.CalculateCommissionCost(rawEntryPrice, quantityToAdd, symbol, interval, currentBar.Timestamp);
                                            double slippageCost = await transactionCostService.CalculateSlippageCost(rawEntryPrice, quantityToAdd, expectedDirection, symbol, interval, currentBar.Timestamp);
                                            double spreadAndOtherCost = await transactionCostService.GetSpreadCost(rawEntryPrice, quantityToAdd, symbol, interval, currentBar.Timestamp);
                                            double totalEntryCostForTranche = commissionCost + slippageCost + spreadAndOtherCost;

                                            await portfolioManager.AddToPosition(symbol, quantityToAdd, effectiveEntryPrice, totalEntryCostForTranche);
                                            currentPosition = portfolioManager.GetOpenPositions().FirstOrDefault(p => p.Symbol == symbol);
                                        }
                                    }
                                }
                            }
                        }

                        if (currentPosition != null && activeTrade != null)
                        {
                            var lastBar = orderedData.LastOrDefault(b => b.Timestamp <= config.EndDate) ?? orderedData.Last();
                            var closedTrade = await ClosePositionAsync(portfolioManager, transactionCostService, performanceCalculator, tradesService, strategyInstance, activeTrade, currentPosition, lastBar, orderedData, symbol, interval, runId, 50, symbolKellyHalfFractions);
                            if (closedTrade != null) allTrades.Add(closedTrade);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception in parallel loop for {Symbol} [{Interval}].", symbol, interval);
                    }
                });

                result.Trades.AddRange(allTrades);
                result.TotalTrades = result.Trades.Count;
                _logger.LogInformation("RunId: {RunId} - Backtest completed. Total trades: {TradeCount}", runId, result.TotalTrades);
                await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
                await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunId: {RunId} - Critical error during backtest execution", runId);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", ex.Message);
                throw;
            }
        }

        private async Task<TradeSummary?> ClosePositionAsync(
            IPortfolioManager portfolioManager, ITransactionCostService transactionCostService,
            IPerformanceCalculator performanceCalculator, ITradeService tradesService,
            ISingleAssetStrategy strategyInstance,
            TradeSummary activeTrade, Position currentPosition, HistoricalPriceModel exitBar, List<HistoricalPriceModel> orderedData,
            string symbol, string interval, Guid runId, int rollingKellyLookbackTrades,
            ConcurrentDictionary<string, double> symbolKellyHalfFractions)
        {
            double rawExitPrice = exitBar.ClosePrice;
            PositionDirection exitDirection = currentPosition.Direction;

            double effectiveExitPrice = await transactionCostService.CalculateExitCost(rawExitPrice, exitDirection, symbol, interval, exitBar.Timestamp);
            double commissionCost = await transactionCostService.CalculateCommissionCost(rawExitPrice, currentPosition.Quantity, symbol, interval, exitBar.Timestamp);
            double slippageCost = await transactionCostService.CalculateSlippageCost(rawExitPrice, currentPosition.Quantity, exitDirection, symbol, interval, exitBar.Timestamp);
            double spreadAndOtherCost = await transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, exitBar.Timestamp);
            double totalExitCost = commissionCost + slippageCost + spreadAndOtherCost;

            double grossPnl = (exitDirection == PositionDirection.Long)
             ? (rawExitPrice - currentPosition.EntryPrice) * currentPosition.Quantity
             : (currentPosition.EntryPrice - rawExitPrice) * currentPosition.Quantity;

            double totalTransactionCost = (activeTrade.TotalTransactionCost ?? 0) + totalExitCost;
            double netPnl = grossPnl - totalTransactionCost;

            var tradeHistoricalData = orderedData.Where(d => d.Timestamp >= activeTrade.EntryDate && d.Timestamp <= exitBar.Timestamp).ToList();
            double maxAdverseExcursion = 0, maxFavorableExcursion = 0;

            double exitSpreadCost = await transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, exitBar.Timestamp);
            double profitLoss = (exitDirection == PositionDirection.Long)
                ? (effectiveExitPrice - activeTrade.EntryPrice) * currentPosition.Quantity
                : (activeTrade.EntryPrice - effectiveExitPrice) * currentPosition.Quantity;

            activeTrade.ExitDate = exitBar.Timestamp;
            activeTrade.ExitPrice = effectiveExitPrice;
            activeTrade.GrossProfitLoss = grossPnl;
            activeTrade.ProfitLoss = netPnl;
            activeTrade.CommissionCost = (activeTrade.CommissionCost ?? 0) + commissionCost;
            activeTrade.SlippageCost = (activeTrade.SlippageCost ?? 0) + slippageCost;
            activeTrade.OtherTransactionCost = (activeTrade.OtherTransactionCost ?? 0) + spreadAndOtherCost;
            activeTrade.TotalTransactionCost = totalTransactionCost;
            activeTrade.HoldingPeriodMinutes = (int)(exitBar.Timestamp - activeTrade.EntryDate).TotalMinutes;
            activeTrade.MaxAdverseExcursion = maxAdverseExcursion;
            activeTrade.MaxFavorableExcursion = maxFavorableExcursion;
            activeTrade.ExitReason = strategyInstance.GetExitReason(in exitBar, orderedData.Slice(0, orderedData.Count), new Dictionary<string, double>()); // You'll need to get indicators here if reason depends on them.

            var closedTrade = await portfolioManager.ClosePosition(activeTrade);

            if (closedTrade != null)
            {
                await tradesService.SaveOrUpdateBacktestTrade(closedTrade);

                var recentTrades = portfolioManager.GetCompletedTradesHistory().Where(t => t.Symbol == symbol && t.Interval == interval).OrderByDescending(t => t.ExitDate).Take(rollingKellyLookbackTrades).ToList();
                var kellyMetrics = performanceCalculator.CalculateKellyMetrics(recentTrades);
                symbolKellyHalfFractions[symbol + "_" + interval] = kellyMetrics.KellyHalfFraction;
                _logger.LogInformation("RunId: {RunId} - Position CLOSED for {Symbol}. PnL: {PnL:C}", runId, symbol, closedTrade.ProfitLoss);
            }
            return closedTrade;
        }


        //[AutomaticRetry(Attempts = 1)]
        //public async Task RunBacktest(string configJson, Guid runId)
        //{
        //    Debugger.Launch();

        //    _logger.LogInformation("Run Backtest started");
        //    var config = JsonSerializer.Deserialize<BacktestConfiguration>(configJson);
        //    if (config == null)
        //    {
        //        await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Failed to deserialize configuration.");
        //        throw new ArgumentException("Could not deserialize backtest configuration from JSON.", nameof(configJson));
        //    }
        //    _logger.LogInformation("Successfully deserialized configuration for RunId: {RunId}", runId);
        //    await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");

        //    var result = new BacktestResult();
        //    try
        //    {
        //        var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(
        //            config.StrategyName, config.InitialCapital, config.StrategyParameters);

        //        if (strategyInstance == null)
        //            throw new InvalidOperationException($"Strategy '{config.StrategyName}' could not be created.");

        //        int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
        //        long minimumAdv = strategyInstance.GetMinimumAverageDailyVolume();
        //        int rollingAdvLookbackBars = 20;
        //        int rollingKellyLookbackTrades = 50;

        //        strategyInstance.Initialize(config.InitialCapital, config.StrategyParameters);
        //        _logger.LogInformation("RunId: {RunId} - Strategy '{StrategyName}' requires minimum lookback of {MinLookback} bars.", runId, config.StrategyName, strategyMinimumLookback);


        //        var testCaseStream = _securitiesOverviewService.StreamSecuritiesForBacktest(config.Symbols, config.Intervals);
        //        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };
        //        var symbolsWithCoverage = new ConcurrentBag<SymbolCoverageBacktestModel>();
        //        var allTrades = new ConcurrentBag<TradeSummary>();
        //        var symbolKellyHalfFractions = new ConcurrentDictionary<string, double>();

        //        await Parallel.ForEachAsync(testCaseStream, parallelOptions, async (testCase, cancellationToken) =>
        //        {
        //            await using var scope = _serviceProvider.CreateAsyncScope();
        //            var portfolioManager = scope.ServiceProvider.GetRequiredService<IPortfolioManager>();
        //            var historicalPriceService = scope.ServiceProvider.GetRequiredService<IHistoricalPriceService>();
        //            var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();
        //            var liquidityService = scope.ServiceProvider.GetRequiredService<ILiquidityService>();
        //            var tradesService = scope.ServiceProvider.GetRequiredService<ITradeService>();
        //            var performanceCalculator = scope.ServiceProvider.GetRequiredService<IPerformanceCalculator>();

        //            await portfolioManager.Initialize(config.InitialCapital);

        //            var symbol = testCase.Symbol.Trim();
        //            var interval = testCase.Interval.Trim();
        //            try
        //            {
        //                var orderedData = (await historicalPriceService.GetHistoricalPrices(symbol, interval))
        //                                    .OrderBy(d => d.Timestamp).ToList();

        //                if (orderedData.Count < strategyMinimumLookback || (orderedData.Last().Timestamp.Year - orderedData.First().Timestamp.Year) < 10) return;

        //                _logger.LogInformation("RunId: {RunId} - Processing {Symbol} [{Interval}] with {Count} bars of data.", 
        //                    runId, symbol, interval, orderedData.Count);

        //                var closePrices = orderedData.Select(p => p.ClosePrice).ToArray();
        //                var movingAverage = _gpuIndicatorService.CalculateSma(closePrices, strategyMinimumLookback);
        //                var standardDeviation = _gpuIndicatorService.CalculateStdDev(closePrices, strategyMinimumLookback);
        //                var rsi = strategyInstance.CalculateRSI(closePrices, strategyMinimumLookback);
        //                var upperBand = new double[closePrices.Length];
        //                var lowerBand = new double[closePrices.Length];
        //                for (int i = 0; i < closePrices.Length; i++)
        //                {
        //                    upperBand[i] = movingAverage[i] + (2 * standardDeviation[i]);
        //                    lowerBand[i] = lowerBand[i] = movingAverage[i] - (2 * standardDeviation[i]);
        //                }

        //                var indicators = new Dictionary<string, double[]>
        //                {
        //                    { "RSI", rsi }, { "UpperBand", upperBand }, { "LowerBand", lowerBand }
        //                };

        //                var timestampIndexMap = orderedData.Select((data, index) => new { data.Timestamp, index }).ToDictionary(x => x.Timestamp, x => x.index);
        //                int backtestStartIndex = orderedData.FindIndex(p => p.Timestamp >= config.StartDate);
        //                if (backtestStartIndex == -1) return;

        //                Position? currentPosition = null;
        //                TradeSummary? activeTrade = null;
        //                double currentSymbolKellyHalfFraction = symbolKellyHalfFractions.GetOrAdd(symbol + "_" + interval, 0.001);

        //                List<TradeSummary> tradesForThisCase = new List<TradeSummary>();

        //                for (int i = backtestStartIndex; i < orderedData.Count; i++)
        //                {
        //                    var currentBar = orderedData[i];
        //                    if (currentBar.Timestamp > config.EndDate) break;
        //                    if (!timestampIndexMap.TryGetValue(currentBar.Timestamp, out var globalIndex) || globalIndex < strategyMinimumLookback) continue;

        //                    bool shouldExit;
        //                    SignalDecision signal;
        //                    double allocationAmount = 0;

        //                    var currentIndicatorValues = new Dictionary<string, double>
        //                    {
        //                        { "RSI", indicators["RSI"][globalIndex] },
        //                        { "UpperBand", indicators["UpperBand"][globalIndex] },
        //                        { "LowerBand", indicators["LowerBand"][globalIndex] }
        //                    };

        //                    {
        //                        ReadOnlySpan<HistoricalPriceModel> dataWindowSpan = CollectionsMarshal.AsSpan(orderedData).Slice(0, i + 1);

        //                        shouldExit = currentPosition != null && strategyInstance.ShouldExitPosition(currentPosition, in currentBar, dataWindowSpan, currentIndicatorValues);
        //                        signal = strategyInstance.GenerateSignal(in currentBar, currentPosition, dataWindowSpan, currentIndicatorValues);

        //                        if (currentPosition == null && signal != SignalDecision.Hold)
        //                        {
        //                            allocationAmount = strategyInstance.GetAllocationAmount(
        //                                in currentBar, dataWindowSpan, currentIndicatorValues,
        //                                config.InitialCapital * 0.02, portfolioManager.GetTotalEquity(), currentSymbolKellyHalfFraction);
        //                        }
        //                    }

        //                    if (currentPosition != null && activeTrade != null && shouldExit)
        //                    {
        //                        _logger.LogInformation("RunId: {RunId} - Exit signal triggered for {Symbol} at {Timestamp}", runId, symbol, currentBar.Timestamp);

        //                        double rawExitPrice = currentBar.ClosePrice;
        //                        PositionDirection exitPositionDirection = currentPosition.Direction;
        //                        double effectiveExitPrice = await transactionCostService.CalculateExitCost(rawExitPrice, exitPositionDirection, symbol, interval, currentBar.Timestamp);
        //                        double exitSpreadCost = await transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, currentBar.Timestamp);

        //                        double profitLoss = (exitPositionDirection == PositionDirection.Long)
        //                            ? (effectiveExitPrice - activeTrade.EntryPrice) * currentPosition.Quantity
        //                            : (activeTrade.EntryPrice - effectiveExitPrice) * currentPosition.Quantity;

        //                        double totalTradeTransactionCost = activeTrade.TransactionCost + exitSpreadCost;

        //                        TradeSummary? closedTrade = await portfolioManager.ClosePosition(
        //                            strategyInstance.Name, symbol, interval, exitPositionDirection,
        //                            currentPosition.Quantity, effectiveExitPrice, currentBar.Timestamp,
        //                            totalTradeTransactionCost, profitLoss
        //                        );

        //                        if (closedTrade != null)
        //                        {
        //                            allTrades.Add(closedTrade);

        //                            var recentTrades = portfolioManager.GetCompletedTradesHistory()
        //                                .Where(t => t.Symbol == symbol && t.Interval == interval)
        //                                .OrderByDescending(t => t.ExitDate).Take(rollingKellyLookbackTrades).ToList();
        //                            KellyMetrics kellyMetrics = performanceCalculator.CalculateKellyMetrics(recentTrades);
        //                            currentSymbolKellyHalfFraction = kellyMetrics.KellyHalfFraction;
        //                            symbolKellyHalfFractions[symbol + "_" + interval] = currentSymbolKellyHalfFraction;
        //                        }

        //                        currentPosition = null;
        //                        activeTrade = null;
        //                    }
        //                    if (currentPosition == null && signal != SignalDecision.Hold && allocationAmount > 0)
        //                    {
        //                        if (!liquidityService.IsSymbolLiquidAtTime(symbol, interval, minimumAdv, currentBar.Timestamp, rollingAdvLookbackBars, orderedData))
        //                            continue;

        //                        if (allocationAmount <= 0) continue;

        //                        double effectiveEntryPrice = await transactionCostService.CalculateEntryCost(currentBar.ClosePrice, signal, symbol, interval, currentBar.Timestamp);
        //                        if (effectiveEntryPrice <= 0) continue;

        //                        int quantity = (int)Math.Round(allocationAmount / effectiveEntryPrice);
        //                        if (quantity <= 0) continue;

        //                        double entrySpreadCost = await transactionCostService.GetSpreadCost(currentBar.ClosePrice, quantity, symbol, interval, currentBar.Timestamp);
        //                        double totalCost = (quantity * effectiveEntryPrice) + entrySpreadCost;

        //                        if (await portfolioManager.CanOpenPosition(totalCost))
        //                        {
        //                            var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;
        //                            await portfolioManager.OpenPosition(symbol, interval, direction, quantity, effectiveEntryPrice, currentBar.Timestamp, entrySpreadCost);

        //                            activeTrade = new TradeSummary
        //                            {
        //                                RunId = runId,
        //                                StrategyName = strategyInstance.Name,
        //                                EntryDate = currentBar.Timestamp,
        //                                EntryPrice = effectiveEntryPrice,
        //                                Direction = direction.ToString(),
        //                                Quantity = quantity,
        //                                Symbol = symbol,
        //                                Interval = interval,
        //                                TransactionCost = entrySpreadCost,
        //                            };

        //                            currentPosition = portfolioManager.GetOpenPositions().FirstOrDefault(p => p.Symbol == symbol && p.EntryDate == currentBar.Timestamp);

        //                            _logger.LogInformation("RunId: {RunId} - Position OPENED: {Direction} {Quantity} {Symbol} @ {Price}", runId, direction, quantity, symbol, effectiveEntryPrice);
        //                        }
        //                    }

        //                    if (currentPosition != null && activeTrade != null)
        //                    {
        //                        var lastBar = orderedData.Last();
        //                        double rawExitPrice = lastBar.ClosePrice;
        //                        PositionDirection exitPositionDirection = currentPosition.Direction;

        //                        double effectiveExitPrice = await transactionCostService.CalculateExitCost(rawExitPrice, exitPositionDirection, symbol, interval, lastBar.Timestamp);
        //                        double exitSpreadCost = await transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, lastBar.Timestamp);

        //                        double profitLossBeforeCosts;
        //                        if (currentPosition.Direction == PositionDirection.Long)
        //                            profitLossBeforeCosts = (effectiveExitPrice - currentPosition.EntryPrice) * currentPosition.Quantity;
        //                        else
        //                            profitLossBeforeCosts = (currentPosition.EntryPrice - effectiveExitPrice) * currentPosition.Quantity;

        //                        double totalTradeTransactionCost = activeTrade.TransactionCost + exitSpreadCost;
        //                        double netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

        //                        await portfolioManager.ClosePosition(strategyInstance.Name, symbol, interval, exitPositionDirection, currentPosition.Quantity, effectiveExitPrice, lastBar.Timestamp, totalTradeTransactionCost, netProfitLoss);

        //                        var recentTradesForKelly = portfolioManager.GetCompletedTradesHistory()
        //                                                                    .Where(t => t.Symbol == symbol && t.Interval == interval)
        //                                                                    .OrderByDescending(t => t.ExitDate)
        //                                                                    .Take(rollingKellyLookbackTrades)
        //                                                                    .ToList();

        //                        KellyMetrics kellyMetrics = performanceCalculator.CalculateKellyMetrics(recentTradesForKelly);
        //                        currentSymbolKellyHalfFraction = kellyMetrics.KellyHalfFraction;
        //                        symbolKellyHalfFractions[symbol + "_" + interval] = currentSymbolKellyHalfFraction;

        //                        _logger.LogDebug("RunId: {RunId} - Symbol {Symbol} [{Interval}] - Kelly/2 updated to {KellyHalf:P2} (WinRate: {WinRate:P2}, Payoff: {Payoff:N2}) after final trade closure. (Trades: {TradeCount})",
        //                            runId, symbol, interval, currentSymbolKellyHalfFraction, kellyMetrics.WinRate, kellyMetrics.PayoffRatio, kellyMetrics.TotalTrades);

        //                        var finalClosedTrade = portfolioManager.GetCompletedTradesHistory()
        //                                                                .LastOrDefault(t => t.Symbol == symbol && t.EntryDate == activeTrade.EntryDate && t.ExitDate == lastBar.Timestamp);
        //                        if (finalClosedTrade != null)
        //                        {
        //                            allTrades.Add(finalClosedTrade);
        //                            await tradesService.SaveBacktestResults(runId, new BacktestResult { Trades = new List<TradeSummary> { finalClosedTrade } }, symbol, interval);
        //                            _logger.LogInformation($"RunId: {runId}, Symbol: {symbol}, Interval: {interval}, Final Position Closed at end of backtest. Timestamp: {lastBar.Timestamp}. Net PnL: {netProfitLoss:C}", runId, symbol, interval, lastBar.Timestamp, netProfitLoss);
        //                        }
        //                        else
        //                        {
        //                            _logger.LogWarning("RunId: {RunId} - Could not find final closed trade for {Symbol} [{Interval}] in PortfolioManager history to save at end of backtest. This may indicate a data discrepancy.", runId, symbol, interval);
        //                        }
        //                    }
        //                }
        //                var tradesForThisSymbol = portfolioManager.GetCompletedTradesHistory();
        //                foreach (var trade in tradesForThisSymbol)
        //                    allTrades.Add(trade);
        //            }
        //            catch (Exception ex)
        //            {
        //                _logger.LogError(ex, "Unhandled exception in parallel loop for {Symbol} [{Interval}]. RunId: {RunId}", symbol, interval, runId);
        //            }
        //        });

        //        result.Trades.AddRange(allTrades);
        //        result.TotalTrades = result.Trades.Count;
        //        _logger.LogInformation("RunId: {RunId} - Backtest completed. Total trades generated: {TradeCount}", runId, result.TotalTrades);

        //        await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
        //        await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
        //        await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "RunId: {RunId} - Error during backtest execution", runId);
        //        await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", ex.Message);
        //        throw;
        //    }
        //    await Task.CompletedTask;
        //}

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
