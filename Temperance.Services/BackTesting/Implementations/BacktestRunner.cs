using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.MarketHealth;
using Temperance.Data.Models.Performance;
using Temperance.Data.Models.Strategy;
using Temperance.Data.Models.Trading;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;
using Temperance.Services.Trading.Strategies.Momentum;
using Temperance.Utilities.Helpers;
using TradingApp.src.Core.Services.Implementations;
using TradingApp.src.Core.Services.Interfaces;
namespace Temperance.Services.BackTesting.Implementations
{
    public class BacktestRunner : IBacktestRunner
    {
        private readonly IBackgroundJobClient _backgroundJobClient;
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
        private readonly IQualityFilterService _qualityFilterService;
        private readonly IMarketHealthService _marketHealthService;
        private readonly IConductorClient _conductorClient;
        private readonly ILogger<BacktestRunner> _logger;

        private readonly ConcurrentDictionary<string, ISingleAssetStrategy> _strategyCache = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, double[]>> _indicatorCache = new();

        public BacktestRunner(
            IBackgroundJobClient backgroundJobClient,
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
            IQualityFilterService qualityFilterService,
            IMarketHealthService marketHealthService,
            IConductorClient conductorClient,
            ILogger<BacktestRunner> logger)
        {
            _backgroundJobClient = backgroundJobClient;
            _liquidityService = liquidityService;
            _transactionCostService = transactionCostService;
            _gpuIndicatorService = gpuIndicatorService;
            _portfolioManager = portfolioManager;
            _strategyFactory = strategyFactory;
            _tradesService = tradesService;
            _securitiesOverviewService = securitiesOverviewService;
            _performanceCalculator = performanceCalculator;
            _serviceProvider = serviceProvider;
            _qualityFilterService = qualityFilterService;
            _marketHealthService = marketHealthService;
            _conductorClient = conductorClient;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task RunBacktest(BacktestConfiguration config, Guid runId)
        {
            if (config == null)
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Configuration error.");
                throw new ArgumentException("Could not deserialize configuration.", nameof(config));
            }

            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");
            var result = new BacktestResult();

            var initialPortfolioState = await _tradesService.GetLatestPortfolioStateAsync(config.SessionId.Value);
            if (initialPortfolioState.HasValue)
                _portfolioManager.HydrateState(initialPortfolioState.Value.Cash, initialPortfolioState.Value.OpenPositions);
            else
                await _portfolioManager.Initialize(config.SessionId.Value, config.InitialCapital);

            try
            {
                var marketHealthCache = new ConcurrentDictionary<DateTime, MarketHealthScore>();
                for (var day = config.StartDate.Date; day <= config.EndDate.Date; day = day.AddDays(1))
                {
                    var score = await _marketHealthService.GetCurrentMarketHealth(day);
                    marketHealthCache.TryAdd(day, score);
                }

                var testCaseStream = _securitiesOverviewService.StreamSecuritiesForBacktest(config.Symbols, config.Intervals);
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };
                var allTrades = new ConcurrentBag<TradeSummary>();
                var symbolKellyHalfFractions = new ConcurrentDictionary<string, double>();

                await Parallel.ForEachAsync(testCaseStream, parallelOptions, async (testCase, cancellationToken) =>
                {
                    var symbol = testCase.Symbol.Trim();
                    var interval = testCase.Interval.Trim();

                    await using var scope = _serviceProvider.CreateAsyncScope();
                    var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(config.StrategyName, config.InitialCapital, config.StrategyParameters);
                    if (strategyInstance == null) return;

                    var historicalPriceService = scope.ServiceProvider.GetRequiredService<IHistoricalPriceService>();
                    var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();
                    var liquidityService = scope.ServiceProvider.GetRequiredService<ILiquidityService>();
                    var performanceCalculator = scope.ServiceProvider.GetRequiredService<IPerformanceCalculator>();
                    var tradesService = scope.ServiceProvider.GetRequiredService<ITradeService>();

                    try
                    {
                        var orderedData = (await historicalPriceService.GetHistoricalPrices(symbol, interval)).OrderBy(d => d.Timestamp).ToList();
                        int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
                        if (orderedData.Count < strategyMinimumLookback) return;

                        var lastBarTimestamps = config.UseMocExit ? orderedData.GroupBy(p => p.Timestamp.Date).Select(g => g.Max(p => p.Timestamp)).ToHashSet() : new HashSet<DateTime>();

                        _logger.LogInformation($"RunId: {runId} - Processing {symbol} [{interval}] with {orderedData.Count} bars.");

                        var highPrices = orderedData.Select(p => p.HighPrice).ToArray();
                        var lowPrices = orderedData.Select(p => p.LowPrice).ToArray();
                        var closePrices = orderedData.Select(p => p.ClosePrice).ToArray();

                        var atrPeriod = ParameterHelper.GetParameterOrDefault(config.StrategyParameters, "AtrPeriod", 14);
                        var stdDevMultiplier = strategyInstance.GetStdDevMultiplier();

                        var movingAverage = _gpuIndicatorService.CalculateSma(closePrices, strategyMinimumLookback);
                        var standardDeviation = _gpuIndicatorService.CalculateStdDev(closePrices, strategyMinimumLookback);
                        var rsi = strategyInstance.CalculateRSI(closePrices, strategyMinimumLookback);
                        var atr = _gpuIndicatorService.CalculateAtr(highPrices, lowPrices, closePrices, atrPeriod);
                        var upperBand = movingAverage.Zip(standardDeviation, (m, s) => m + (stdDevMultiplier * s)).ToArray();
                        var lowerBand = movingAverage.Zip(standardDeviation, (m, s) => m - (stdDevMultiplier * s)).ToArray();

                        var indicators = new Dictionary<string, double[]>
                        {
                            { "RSI", rsi }, { "UpperBand", upperBand }, { "LowerBand", lowerBand },
                            { "ATR", atr }, { "SMA", movingAverage }
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
                                { "RSI", indicators["RSI"][globalIndex] }, { "UpperBand", indicators["UpperBand"][globalIndex] },
                                { "LowerBand", indicators["LowerBand"][globalIndex] }, { "ATR", indicators["ATR"][globalIndex] },
                                { "SMA", indicators["SMA"][globalIndex] }
                            };

                            if (currentPosition != null) { currentPosition.BarsHeld++; }

                            marketHealthCache.TryGetValue(currentBar.Timestamp.Date, out var currentMarketHealth);
                            var dataWindow = orderedData.Take(i + 1).ToList();

                            if (currentPosition != null && activeTrade != null)
                            {
                                string exitReason = strategyInstance.GetExitReason(currentPosition, in currentBar, dataWindow, currentIndicatorValues);
                                bool isMocBar = config.UseMocExit && lastBarTimestamps.Contains(currentBar.Timestamp);
                                if (isMocBar && exitReason == "Hold") { exitReason = "Market on Close"; }

                                if (exitReason != "Hold")
                                {
                                    var closedTrade = await ClosePositionAsync(_portfolioManager, transactionCostService, performanceCalculator, tradesService, activeTrade, currentPosition, currentBar, symbol, interval, runId, 50, symbolKellyHalfFractions, exitReason, currentIndicatorValues);
                                    if (closedTrade != null) { allTrades.Add(closedTrade); }
                                    currentPosition = null;
                                    activeTrade = null;
                                    continue;
                                }
                            }

                            var signal = strategyInstance.GenerateSignal(in currentBar, currentPosition, dataWindow, currentIndicatorValues, currentMarketHealth);
                            if (signal != SignalDecision.Hold && currentPosition == null)
                            {
                                long minimumAdv = strategyInstance.GetMinimumAverageDailyVolume();
                                if (!liquidityService.IsSymbolLiquidAtTime(symbol, interval, minimumAdv, currentBar.Timestamp, 20, orderedData)) continue;

                                double allocationAmount = strategyInstance.GetAllocationAmount(in currentBar, dataWindow, currentIndicatorValues, config.InitialCapital * 0.02, _portfolioManager.GetTotalEquity(), currentSymbolKellyHalfFraction, 1, currentMarketHealth);
                                if (allocationAmount > 0)
                                {
                                    double rawEntryPrice = currentBar.ClosePrice;
                                    int quantity = (int)Math.Floor(allocationAmount / rawEntryPrice);
                                    if (quantity <= 0) continue;

                                    var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;
                                    double effectiveEntryPrice = await transactionCostService.CalculateEntryCost(rawEntryPrice, signal, symbol, interval, currentBar.Timestamp);
                                    double totalEntryCost = await transactionCostService.GetSpreadCost(rawEntryPrice, quantity, symbol, interval, currentBar.Timestamp);
                                    double totalCashOutlay = (quantity * rawEntryPrice) + totalEntryCost;

                                    if (await _portfolioManager.CanOpenPosition(totalCashOutlay))
                                    {
                                        await _portfolioManager.OpenPosition(symbol, interval, direction, quantity, rawEntryPrice, currentBar.Timestamp, totalEntryCost);
                                        activeTrade = new TradeSummary
                                        {
                                            Id = Guid.NewGuid(),
                                            RunId = runId,
                                            StrategyName = strategyInstance.Name,
                                            EntryDate = currentBar.Timestamp,
                                            EntryPrice = rawEntryPrice,
                                            Direction = direction.ToString(),
                                            Quantity = quantity,
                                            Symbol = symbol,
                                            Interval = interval,
                                            TotalTransactionCost = totalEntryCost,
                                            EntryReason = strategyInstance.GetEntryReason(in currentBar, dataWindow, currentIndicatorValues)
                                        };
                                        currentPosition = _portfolioManager.GetOpenPositions().FirstOrDefault(p => p.Symbol == symbol && p.InitialEntryDate == currentBar.Timestamp);

                                        if (currentPosition != null)
                                        {
                                            double entryAtr = currentIndicatorValues["ATR"];
                                            currentPosition.StopLossPrice = (direction == PositionDirection.Long)
                                                ? currentPosition.AverageEntryPrice - (strategyInstance.GetAtrMultiplier() * entryAtr)
                                                : currentPosition.AverageEntryPrice + (strategyInstance.GetAtrMultiplier() * entryAtr);
                                        }
                                        await tradesService.SaveOrUpdateBacktestTrade(activeTrade);
                                    }
                                }
                            }
                        }

                        if (currentPosition != null && activeTrade != null)
                        {
                            var lastBar = orderedData.LastOrDefault(b => b.Timestamp <= config.EndDate) ?? orderedData.Last();
                            var lastBarIndex = timestampIndexMap[lastBar.Timestamp];
                            var lastIndicators = new Dictionary<string, double>
                            {
                                { "RSI", indicators["RSI"][lastBarIndex] }, { "ATR", indicators["ATR"][lastBarIndex] },
                                { "SMA", indicators["SMA"][lastBarIndex] }
                            };
                            var closedTrade = await ClosePositionAsync(_portfolioManager, transactionCostService, _performanceCalculator, _tradesService, activeTrade, currentPosition, lastBar, symbol, interval, runId, 50, symbolKellyHalfFractions, "End of Backtest", lastIndicators);
                            if (closedTrade != null) { allTrades.Add(closedTrade); }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception in parallel loop for {Symbol} [{Interval}].", symbol, interval);
                    }
                });

                result.Trades.AddRange(allTrades);
                result.TotalTrades = result.Trades.Count;
                result.OptimizationResultId = config.OptimizationResultId;
                _logger.LogInformation("RunId: {RunId} - Backtest completed. Total trades: {TradeCount}", runId, result.TotalTrades);
                await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
                await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");

                double finalEquity = _portfolioManager.GetTotalEquity();

                var completionPayload = new BacktestCompletionPayload
                {
                    RunId = runId,
                    SessionId = config.SessionId,
                    FinalEquity = finalEquity,
                    StrategyName = config.StrategyName,
                    Symbol = config.Symbols.FirstOrDefault(),
                    Interval = config.Intervals.FirstOrDefault(),
                    LastBacktestEndDate = config.EndDate,
                    TotalReturn = result.TotalReturn,
                    SharpeRatio = result.SharpeRatio,
                    MaxDrawdown = result.MaxDrawdown,
                    TotalTrades = result.TotalTrades
                };

                _logger.LogInformation("Notifying backtest complete...");
                await _conductorClient.NotifyBacktestCompleteAsync(completionPayload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during backtest {RunId}", runId);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", ex.Message);
                throw;
            }
        }

        private async Task ProcessSleeveForTimestamp(WalkForwardSleeve sleeve, HistoricalPriceModel currentBar, List<HistoricalPriceModel> historicalWindow, IPortfolioManager portfolioManager, IServiceScope scope)
        {
            var liquidityService = scope.ServiceProvider.GetRequiredService<ILiquidityService>();
            var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();

            var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(sleeve.OptimizedParametersJson) ?? new();
            var strategy = GetStrategyInstance(sleeve, portfolioManager.GetTotalEquity());
            if (strategy == null) return;

            var indicatorValues = GetIndicatorValuesForTimestamp(sleeve.Symbol, currentBar.Timestamp);
            var position = portfolioManager.GetOpenPositions().FirstOrDefault(p => p.Symbol == sleeve.Symbol);

            if (position != null) position.BarsHeld++;

            if (position != null)
            {
                var exitReason = strategy.GetExitReason(position, in currentBar, historicalWindow, indicatorValues);
                if (exitReason != "Hold")
                {
                    // Find the original trade summary to update it upon closing
                    var activeTrade = portfolioManager.GetCompletedTradesHistory()
                        .FirstOrDefault(t => t.Symbol == position.Symbol && !t.ExitDate.HasValue);

                    if (activeTrade != null)
                    {
                        await ClosePositionAsync(portfolioManager, transactionCostService, _performanceCalculator, _tradesService,
                            activeTrade, position, currentBar, sleeve.Symbol, sleeve.Interval, sleeve.SessionId,
                            50, new ConcurrentDictionary<string, double>(), exitReason, indicatorValues);
                    }
                    return;
                }
            }

            // --- ENTRY LOGIC ---
            if (position == null)
            {
                var signal = strategy.GenerateSignal(in currentBar, null, historicalWindow, indicatorValues, MarketHealthScore.Neutral);
                if (signal != SignalDecision.Hold)
                {
                    if (!liquidityService.IsSymbolLiquidAtTime(sleeve.Symbol, sleeve.Interval, strategy.GetMinimumAverageDailyVolume(), currentBar.Timestamp, 20, historicalWindow)) return;

                    double allocationAmount = strategy.GetAllocationAmount(in currentBar, historicalWindow, indicatorValues, (portfolioManager.GetTotalEquity() * 0.02), portfolioManager.GetTotalEquity(), 0.01, 1, MarketHealthScore.Neutral);
                    if (allocationAmount <= 0) return;

                    int quantity = (int)Math.Floor(allocationAmount / currentBar.ClosePrice);
                    if (quantity <= 0) return;

                    var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;
                    double totalEntryCost = await transactionCostService.GetSpreadCost(currentBar.ClosePrice, quantity, sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);
                    double totalCashOutlay = (quantity * currentBar.ClosePrice) + totalEntryCost;

                    if (await portfolioManager.CanOpenPosition(totalCashOutlay))
                    {
                        await portfolioManager.OpenPosition(sleeve.Symbol, sleeve.Interval, direction, quantity, currentBar.ClosePrice, currentBar.Timestamp, totalEntryCost);

                        var newTrade = new TradeSummary
                        {
                            Id = Guid.NewGuid(),
                            RunId = sleeve.SessionId, // Use SessionId as the RunId for context
                            StrategyName = strategy.Name,
                            EntryDate = currentBar.Timestamp,
                            EntryPrice = currentBar.ClosePrice,
                            Direction = direction.ToString(),
                            Quantity = quantity,
                            Symbol = sleeve.Symbol,
                            Interval = sleeve.Interval,
                            TotalTransactionCost = totalEntryCost,
                            EntryReason = strategy.GetEntryReason(in currentBar, historicalWindow, indicatorValues)
                        };

                        await _tradesService.SaveOrUpdateBacktestTrade(newTrade);
                    }
                }
            }
        }

        // This is the helper method from your original RunBacktest, now part of this class
        private async Task<TradeSummary?> ClosePositionAsync(
            IPortfolioManager portfolioManager, ITransactionCostService transactionCostService,
            IPerformanceCalculator performanceCalculator, ITradeService tradesService,
            TradeSummary activeTrade, Position currentPosition, HistoricalPriceModel exitBar,
            string symbol, string interval, Guid runId, int rollingKellyLookbackTrades,
            ConcurrentDictionary<string, double> symbolKellyHalfFractions, string exitReason,
            Dictionary<string, double> currentIndicatorValues)
        {
            // ... The exact same implementation of this method from your original BacktestRunner.cs ...
            // This logic is sound and can be reused directly.
            double rawExitPrice;

            if (exitReason.StartsWith("ATR Stop-Loss"))
                rawExitPrice = currentPosition.StopLossPrice;
            else if (exitReason.StartsWith("Profit Target"))
                rawExitPrice = currentIndicatorValues["SMA"];
            else
                rawExitPrice = exitBar.ClosePrice;

            PositionDirection exitDirection = currentPosition.Direction;
            double effectiveExitPrice = await transactionCostService.CalculateExitCost(rawExitPrice, exitDirection, symbol, interval, exitBar.Timestamp);

            double commissionCost = await transactionCostService.CalculateCommissionCost(rawExitPrice, currentPosition.Quantity, symbol, interval, exitBar.Timestamp);
            double slippageCost = await transactionCostService.CalculateSlippageCost(rawExitPrice, currentPosition.Quantity, exitDirection, symbol, interval, exitBar.Timestamp);
            double spreadAndOtherCost = await transactionCostService.GetSpreadCost(rawExitPrice, currentPosition.Quantity, symbol, interval, exitBar.Timestamp);
            double totalExitCost = commissionCost + slippageCost + spreadAndOtherCost;

            double grossPnl = (exitDirection == PositionDirection.Long)
                 ? (rawExitPrice - currentPosition.AverageEntryPrice) * currentPosition.Quantity
                 : (currentPosition.AverageEntryPrice - rawExitPrice) * currentPosition.Quantity;

            double totalTransactionCost = (activeTrade.TotalTransactionCost ?? 0) + totalExitCost;
            double netPnl = grossPnl - totalTransactionCost;

            activeTrade.ExitDate = exitBar.Timestamp;
            activeTrade.ExitPrice = effectiveExitPrice;
            activeTrade.GrossProfitLoss = grossPnl;
            activeTrade.ProfitLoss = netPnl;
            activeTrade.CommissionCost = (activeTrade.CommissionCost ?? 0) + commissionCost;
            activeTrade.SlippageCost = (activeTrade.SlippageCost ?? 0) + slippageCost;
            activeTrade.OtherTransactionCost = (activeTrade.OtherTransactionCost ?? 0) + spreadAndOtherCost;
            activeTrade.TotalTransactionCost = totalTransactionCost;
            activeTrade.HoldingPeriodMinutes = (int)(exitBar.Timestamp - activeTrade.EntryDate).TotalMinutes;
            activeTrade.ExitReason = exitReason;

            activeTrade.EntryPrice = currentPosition.AverageEntryPrice;

            var closedTrade = await portfolioManager.ClosePosition(activeTrade);

            if (closedTrade != null)
            {
                await tradesService.SaveOrUpdateBacktestTrade(closedTrade);
            }
            return closedTrade;
        }

        // --- Caching and Helper Methods ---
        private ISingleAssetStrategy? GetStrategyInstance(WalkForwardSleeve sleeve, double currentEquity)
        {
            if (_strategyCache.TryGetValue(sleeve.Symbol, out var cachedStrategy))
            {
                return cachedStrategy;
            }

            var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(sleeve.OptimizedParametersJson) ?? new();
            var strategy = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(sleeve.StrategyName, currentEquity, parameters);
            if (strategy != null)
            {
                _strategyCache.TryAdd(sleeve.Symbol, strategy);
            }
            return strategy;
        }

        private Dictionary<string, double> GetIndicatorValuesForTimestamp(string symbol, DateTime timestamp)
        {
            var values = new Dictionary<string, double>();
            if (_indicatorCache.TryGetValue(symbol, out var symbolIndicators))
            {
                if (symbolIndicators.TryGetValue("TimestampMap", out var timestampMapping))
                {
                    // This is a simplified lookup. A more robust implementation would use a direct map.
                    // For now, we find the index.
                    var timestamps = symbolIndicators["Timestamps"];
                    int index = Array.IndexOf(timestamps, (double)timestamp.ToOADate());
                    if (index != -1)
                    {
                        foreach (var kvp in symbolIndicators)
                        {
                            if (kvp.Key != "Timestamps")
                            {
                                values[kvp.Key] = kvp.Value[index];
                            }
                        }
                    }
                }
            }
            return values;
        }

        private async Task<Dictionary<string, (Dictionary<DateTime, HistoricalPriceModel> Data, Dictionary<string, double[]> Indicators)>> PreloadAndCalculateIndicators(List<string> symbols, string interval, DateTime startDate, DateTime endDate, List<WalkForwardSleeve> sleeves)
        {
            var cache = new Dictionary<string, (Dictionary<DateTime, HistoricalPriceModel> Data, Dictionary<string, double[]> Indicators)>();

            foreach (var symbol in symbols)
            {
                var sleeve = sleeves.First(s => s.Symbol == symbol);
                var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(sleeve.OptimizedParametersJson);
                var strategy = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(sleeve.StrategyName, 100000, parameters); // Temp init

                var data = await _historicalPriceService.GetHistoricalPrices(symbol, interval, startDate.AddDays(-strategy.GetRequiredLookbackPeriod()), endDate);
                if (!data.Any()) continue;

                var dataDict = data.ToDictionary(d => d.Timestamp, d => d);

                // Pre-calculate indicators
                var closePrices = data.Select(p => p.ClosePrice).ToArray();
                var highPrices = data.Select(p => p.HighPrice).ToArray();
                var lowPrices = data.Select(p => p.LowPrice).ToArray();
                var timestamps = data.Select(p => (double)p.Timestamp.ToOADate()).ToArray(); // For lookup

                var sma = _gpuIndicatorService.CalculateSma(closePrices, strategy.GetRequiredLookbackPeriod());
                var stdDev = _gpuIndicatorService.CalculateStdDev(closePrices, strategy.GetRequiredLookbackPeriod());
                var rsi = strategy.CalculateRSI(closePrices, strategy.GetRequiredLookbackPeriod()); // Assuming RSI period is same as lookback
                var atr = _gpuIndicatorService.CalculateAtr(highPrices, lowPrices, closePrices, 14); // Assuming ATR period of 14

                var indicators = new Dictionary<string, double[]>
                {
                    { "SMA", sma },
                    { "StdDev", stdDev },
                    { "RSI", rsi },
                    { "ATR", atr },
                    { "LowerBand", sma.Zip(stdDev, (m, s) => m - (strategy.GetStdDevMultiplier() * s)).ToArray() },
                    { "UpperBand", sma.Zip(stdDev, (m, s) => m + (strategy.GetStdDevMultiplier() * s)).ToArray() },
                    { "Timestamps", timestamps } // For index lookup
                };

                cache[symbol] = (dataDict, indicators);
                _indicatorCache[symbol] = indicators;
            }
            return cache;
        }

        [AutomaticRetry(Attempts = 1)]
        //public async Task RunDualMomentumBacktest(string configJson, Guid runId)
        //{
        //    var config = JsonSerializer.Deserialize<DualMomentumBacktestConfiguration>(configJson);
        //    if (config == null || !config.RiskAssetSymbols.Any() || string.IsNullOrWhiteSpace(config.SafeAssetSymbol))
        //    {
        //        await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Invalid configuration for Dual Momentum Backtest.");
        //        throw new ArgumentException("Invalid configuration for Dual Momentum Backtest.");
        //    }

        //    await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");
        //    _logger.LogInformation("Starting Dual Momentum backtest for RunId: {RunId}", runId);

        //    await _portfolioManager.Initialize(config.InitialCapital);
        //    var allTrades = new ConcurrentBag<TradeSummary>();
        //    var riskAssetKellyHalfFractions = new ConcurrentDictionary<string, double>();

        //    string strategyParametersJson = JsonSerializer.Serialize(config.StrategyParameters);
        //    var strategyInstance = _strategyFactory.CreateStrategy<IDualMomentumStrategy>(
        //        config.StrategyName, config.InitialCapital, config.StrategyParameters);

        //    if (strategyInstance == null)
        //        throw new InvalidOperationException($"Could not create a valid IDualMomentumStrategy for '{config.StrategyName}'.");

        //    int lookbackPeriod = config.MomentumLookbackMonths;
        //    var testCases = config.RiskAssetSymbols.Select(symbol => new { Symbol = symbol }).ToList();

        //    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };
        //    await Parallel.ForEachAsync(testCases, parallelOptions, async (testCase, cancellationToken) =>
        //    {
        //        var dmStrategy = strategyInstance as DualMomentumStrategy;
        //        if (dmStrategy == null)
        //        {
        //            _logger.LogError("RunId: {RunId} - Strategy instance is not a valid DualMomentumStrategy.", runId);
        //            return;
        //        }

        //        var assetDataCache = new Dictionary<string, List<HistoricalPriceModel>>();
        //        var allPortfolioAssets = new List<string>(config.RiskAssetSymbols) { config.SafeAssetSymbol };
        //        foreach (var symbol in allPortfolioAssets)
        //        {
        //            var data = await _historicalPriceService.GetHistoricalPrices(symbol, string.Empty);
        //            assetDataCache[symbol] = data.OrderBy(d => d.Timestamp).ToList();
        //        }
        //    });
        //}

        //public async Task RunPairsBacktest(PairsBacktestConfiguration config, Guid runId)
        //{
        //    var result = new BacktestResult();
        //    // --- Initial Setup ---
        //    await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");
        //    _logger.LogInformation("Starting GPU-accelerated pairs backtest for RunId: {RunId}", runId);

        //    await _portfolioManager.Initialize(config.InitialCapital);
        //    var allTrades = new ConcurrentBag<TradeSummary>();
        //    var pairKellyHalfFractions = new ConcurrentDictionary<string, double>();

        //    string strategyParametersJson = JsonSerializer.Serialize(config.StrategyParameters);

        //    Dictionary<string, object> strategyParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(strategyParametersJson)
        //        ?? new Dictionary<string, object>();

        //    var strategyInstance = _strategyFactory.CreateStrategy<IPairTradingStrategy>(
        //        config.StrategyName, config.InitialCapital, strategyParameters);

        //    if (strategyInstance == null)
        //        throw new InvalidOperationException($"Could not create a valid IPairTradingStrategy for '{config.StrategyName}'.");

        //    int lookbackPeriod = strategyInstance.GetRequiredLookbackPeriod();
        //    int rollingKellyLookbackTrades = 50;
        //    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = config.MaxParallelism };

        //    //await Parallel.ForEachAsync(config.PairsToTest, parallelOptions, async (pair, cancellationToken) =>
        //    //{
        //    foreach (var pair in config.PairsToTest)
        //    {
        //        var pairIdentifier = $"{pair.SymbolA}/{pair.SymbolB}";
        //        try
        //        {
        //            // --- Stage 1: Data Fetching, Alignment, and Full Indicator Pre-calculation ---
        //            var historicalDataA = await _historicalPriceService.GetHistoricalPrices(pair.SymbolA, config.Interval);
        //            var historicalDataB = await _historicalPriceService.GetHistoricalPrices(pair.SymbolB, config.Interval);

        //            var alignedData = AlignData(historicalDataA, historicalDataB);
        //            if (alignedData.Count < lookbackPeriod)
        //            {
        //                _logger.LogWarning("Insufficient aligned data for pair {Pair} to meet lookback of {Lookback}", pairIdentifier, lookbackPeriod);
        //                return;
        //            }

        //            // Extract aligned close prices into arrays for GPU processing
        //            var closePricesA = alignedData.Select(d => (double)d.Item1.ClosePrice).ToArray();
        //            var closePricesB = alignedData.Select(d => (double)d.Item2.ClosePrice).ToArray();

        //            // 1. Calculate the entire spread series
        //            var spreadSeries = new double[alignedData.Count];
        //            for (int i = 0; i < alignedData.Count; i++)
        //                spreadSeries[i] = closePricesA[i] - (pair.HedgeRatio * closePricesB[i]);

        //            var spreadSma = _gpuIndicatorService.CalculateSma(spreadSeries, lookbackPeriod);
        //            var spreadStdDev = _gpuIndicatorService.CalculateStdDev(spreadSeries, lookbackPeriod);

        //            var zScoreSeries = new double[alignedData.Count];
        //            for (int i = 0; i < alignedData.Count; i++)
        //            {
        //                if (spreadStdDev[i] != 0)
        //                    zScoreSeries[i] = (spreadSeries[i] - spreadSma[i]) / spreadStdDev[i];
        //                else
        //                    zScoreSeries[i] = 0;
        //            }

        //            var backtestData = alignedData
        //                .Where(d => d.Item1.Timestamp >= config.StartDate && d.Item1.Timestamp <= config.EndDate).ToList();

        //            var timestampIndexMap = alignedData.Select((data, index) => new { data.Item1.Timestamp, index })
        //                                             .ToDictionary(x => x.Timestamp, x => x.index);

        //            ActivePairTrade? activePairTrade = null;
        //            double currentPairKellyHalfFraction = pairKellyHalfFractions.GetOrAdd(pairIdentifier, 0.01);

        //            foreach (var (currentBarA, currentBarB) in backtestData)
        //            {
        //                if (!timestampIndexMap.TryGetValue(currentBarA.Timestamp, out var globalIndex) || globalIndex < lookbackPeriod)
        //                    continue;

        //                var currentIndicatorValues = new Dictionary<string, double>
        //                    {
        //                        { "ZScore", zScoreSeries[globalIndex] }
        //                    };

        //                // --- Decision Making ---
        //                var signal = strategyInstance.GenerateSignal(currentBarA, currentBarB, currentIndicatorValues);

        //                if (activePairTrade != null && strategyInstance.ShouldExitPosition(new Position { Direction = activePairTrade.Direction, EntryDate = activePairTrade.EntryDate }, currentBarA, currentBarB, currentIndicatorValues))
        //                {
        //                    _logger.LogInformation("RunId: {RunId}, Pair: {Pair}, Attempting to close position at Timestamp: {Timestamp}", runId, pairIdentifier, currentBarA.Timestamp);

        //                    var rawExitPriceA = currentBarA.ClosePrice;
        //                    var directionA = activePairTrade.Direction == PositionDirection.Long ? PositionDirection.Long : PositionDirection.Short;
        //                    var effectiveExitPriceA = await _transactionCostService.CalculateExitCost(rawExitPriceA, directionA, pair.SymbolA, config.Interval, currentBarA.Timestamp);
        //                    var exitSpreadCostA = await _transactionCostService.GetSpreadCost(rawExitPriceA, (int)activePairTrade.QuantityA, pair.SymbolA, config.Interval, currentBarA.Timestamp);

        //                    var rawExitPriceB = currentBarB.ClosePrice;
        //                    var directionB = activePairTrade.Direction == PositionDirection.Long ? PositionDirection.Short : PositionDirection.Long;
        //                    var effectiveExitPriceB = await _transactionCostService.CalculateExitCost(rawExitPriceB, directionB, pair.SymbolB, config.Interval, currentBarB.Timestamp);
        //                    var exitSpreadCostB = await _transactionCostService.GetSpreadCost(rawExitPriceB, (int)activePairTrade.QuantityB, pair.SymbolB, config.Interval, currentBarB.Timestamp);

        //                    var pnlA = (directionA == PositionDirection.Long) ? (effectiveExitPriceA - activePairTrade.EntryPriceA) * activePairTrade.QuantityA : (activePairTrade.EntryPriceA - effectiveExitPriceA) * activePairTrade.QuantityA;
        //                    var pnlB = (directionB == PositionDirection.Long) ? (effectiveExitPriceB - activePairTrade.EntryPriceB) * activePairTrade.QuantityB : (activePairTrade.EntryPriceB - effectiveExitPriceB) * activePairTrade.QuantityB;

        //                    var profitLossBeforeCosts = pnlA + pnlB;
        //                    var totalExitTransactionCost = exitSpreadCostA + exitSpreadCostB;
        //                    var totalTradeTransactionCost = activePairTrade.TotalEntryTransactionCost + totalExitTransactionCost;
        //                    var netProfitLoss = profitLossBeforeCosts - totalTradeTransactionCost;

        //                    var finalizedClosedTrade = await _portfolioManager.ClosePairPosition(activePairTrade, effectiveExitPriceA, effectiveExitPriceB, currentBarA.Timestamp, totalTradeTransactionCost);

        //                    var recentTradesForKelly = _portfolioManager.GetCompletedTradesHistory().Where(t => t.Symbol == pairIdentifier && t.Interval == config.Interval).OrderByDescending(t => t.ExitDate).Take(rollingKellyLookbackTrades).ToList();
        //                    KellyMetrics kellyMetrics = _performanceCalculator.CalculateKellyMetrics(recentTradesForKelly);
        //                    currentPairKellyHalfFraction = kellyMetrics.KellyHalfFraction;
        //                    pairKellyHalfFractions[pairIdentifier] = currentPairKellyHalfFraction;

        //                    if (finalizedClosedTrade != null)
        //                    {
        //                        allTrades.Add(finalizedClosedTrade);
        //                        _logger.LogInformation("RunId: {RunId}, Pair: {Pair}, Position Closed. PnL: {PnL:C}", runId, pairIdentifier, netProfitLoss);
        //                    }
        //                    activePairTrade = null;
        //                }

        //                // --- Open Position Logic ---
        //                if (activePairTrade != null && signal != SignalDecision.Hold)
        //                {
        //                    // This block for opening a position (sizing, calculating costs, updating portfolio)
        //                    // also remains the same.
        //                    #region Open Position Logic
        //                    double allocation = _portfolioManager.GetTotalEquity() * currentPairKellyHalfFraction;
        //                    if (allocation <= 0) continue;

        //                    long quantityA = (long)(allocation / currentBarA.ClosePrice);
        //                    long quantityB = (long)((quantityA * (long)currentBarA.ClosePrice * pair.HedgeRatio) / (long)currentBarB.ClosePrice);
        //                    if (quantityA <= 0 || quantityB <= 0) continue;

        //                    var directionA = signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short;
        //                    var effectiveEntryPriceA = await _transactionCostService.CalculateEntryCost(currentBarA.ClosePrice, signal, pair.SymbolA, config.Interval, currentBarA.Timestamp);
        //                    var entrySpreadCostA = await _transactionCostService.GetSpreadCost(currentBarA.ClosePrice, quantityA, pair.SymbolA, config.Interval, currentBarA.Timestamp);

        //                    var directionB = signal == SignalDecision.Buy ? PositionDirection.Short : PositionDirection.Long;
        //                    var effectiveEntryPriceB = await _transactionCostService.CalculateEntryCost(currentBarB.ClosePrice, signal, pair.SymbolB, config.Interval, currentBarB.Timestamp);
        //                    var entrySpreadCostB = await _transactionCostService.GetSpreadCost(currentBarB.ClosePrice, quantityB, pair.SymbolB, config.Interval, currentBarB.Timestamp);

        //                    double totalCostToOpen = (quantityA * effectiveEntryPriceA) + (quantityB * effectiveEntryPriceB) + entrySpreadCostA + entrySpreadCostB;
        //                    if (!await _portfolioManager.CanOpenPosition(totalCostToOpen)) continue;

        //                    activePairTrade = new ActivePairTrade(pair.SymbolA, pair.SymbolB, (long)pair.HedgeRatio, (signal == SignalDecision.Buy ? PositionDirection.Long : PositionDirection.Short), quantityA, quantityB, effectiveEntryPriceA, effectiveEntryPriceB, currentBarA.Timestamp, entrySpreadCostA + entrySpreadCostB);
        //                    await _portfolioManager.OpenPairPosition(strategyInstance.Name, pairIdentifier, config.Interval, activePairTrade);
        //                    _logger.LogInformation("RunId: {RunId}, Pair: {Pair}, Position Opened. Direction: {Direction}", runId, pairIdentifier, activePairTrade.Direction);
        //                    #endregion
        //                }
        //            }
        //            // Logic to close any trade still open at the end of the test period would go here.
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError(ex, "RunId: {RunId} - Unhandled error processing pair {Pair}", runId, pairIdentifier);
        //        }
        //        //});
        //    }

        //    if (allTrades.Any())
        //    {
        //        foreach (var trade in allTrades)
        //        {
        //            trade.RunId = runId;
        //        }
        //        await _backtestRepository.SaveBacktestTradesAsync(runId, allTrades);
        //        _logger.LogInformation("RunId: {RunId} - Saved {TradeCount} trades for pairs backtest.", runId, allTrades.Count);
        //    }

        //    // --- Finalization ---
        //    result.Trades.AddRange(allTrades);
        //    result.TotalTrades = result.Trades.Count;
        //    await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
        //    await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
        //    await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");
        //}

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
