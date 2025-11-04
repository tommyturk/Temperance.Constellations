using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Text.Json;
using Temperance.Conductor.Repository.Interfaces;
using Temperance.Data.Data.Repositories.Trade.Implementations;
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Models.Backtest;
using Temperance.Data.Models.HistoricalData;
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
using TradingApp.src.Core.Services.Interfaces;
namespace Temperance.Services.BackTesting.Implementations
{
    public class BacktestRunner : IBacktestRunner
    {
        private readonly ILogger<BacktestRunner> _logger;

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
        private readonly IWalkForwardRepository _walkForwardRepository;

        private readonly ConcurrentDictionary<string, ISingleAssetStrategy> _strategyCache = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, double[]>> _indicatorCache = new();

        public BacktestRunner(
            ILogger<BacktestRunner> logger,
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
            IHistoricalPriceService historicalPriceService,
            IWalkForwardRepository walkForwardRepository)
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
            _historicalPriceService = historicalPriceService;
            _logger = logger;
            _walkForwardRepository = walkForwardRepository;
        }

        private record StrategySleeve(
            string Symbol,
            string Interval,
            ISingleAssetStrategy Strategy,
            List<HistoricalPriceModel> PriceData,
            Dictionary<string, double[]> Indicators,
            Dictionary<DateTime, int> TimestampIndexMap,
            HashSet<DateTime> MocExitTimestamps
        )
        {
            public Position? CurrentPosition { get; set; }
            public TradeSummary? ActiveTrade { get; set; }
        }

        private record BacktestSleeve(
            string Symbol,
            string Interval,
            ISingleAssetStrategy Strategy,
            List<HistoricalPriceModel> PriceData,
            Dictionary<string, double[]> Indicators,
            Dictionary<DateTime, int> TimestampIndexMap,
            HashSet<DateTime> MocExitTimestamps
        )
        {
            public Position? CurrentPosition { get; set; }
            public TradeSummary? ActiveTrade { get; set; }
        }

        [Queue("constellations_backtest")]
        [AutomaticRetry(Attempts = 1)]
        public async Task RunPortfolioBacktest(
            Guid sessionId,
            string strategyName,
            DateTime currentOosStartDate,
            DateTime currentOosEndDate,
            Dictionary<string, string> staticParameters,
            List<HistoricalPriceModel> oosMarketData,
            DateTime totalEndDate)
        {
            _logger.LogInformation(
                "BacktestRunner starting for Session {SessionId}, Cycle {Start} to {End}",
                sessionId, currentOosStartDate.ToShortDateString(), currentOosEndDate.ToShortDateString());

            var cycleRunId = Guid.NewGuid(); // A unique ID for this 6-month run
            var allTrades = new ConcurrentBag<TradeSummary>();

            try
            {
                // 1. Load Session & Initialize Portfolio
                var session = await _walkForwardRepository.GetSessionAsync(sessionId);
                var latestPortfolioState = await _walkForwardRepository.GetLatestPortfolioStateAsync(sessionId);

                if (latestPortfolioState != null)
                {
                    var positions = JsonConvert.DeserializeObject<IEnumerable<Position>>(latestPortfolioState.OpenPositionsJson);
                    _portfolioManager.HydrateState(latestPortfolioState.Cash.Value, positions);
                }
                else
                {
                    await _portfolioManager.Initialize(sessionId, session.InitialCapital);
                }

                // 2. Pre-cache Market Health (from your old logic)
                var marketHealthCache = new ConcurrentDictionary<DateTime, MarketHealthScore>();
                for (var day = currentOosStartDate.Date; day <= currentOosEndDate.Date; day = day.AddDays(1))
                {
                    var score = await _marketHealthService.GetCurrentMarketHealth(day);
                    marketHealthCache.TryAdd(day, score);
                }

                // 3. Prepare Sleeves
                var sleeves = new List<BacktestSleeve>();
                var symbols = staticParameters.Keys.ToList();

                foreach (var symbol in symbols)
                {
                    var sleeve = await PrepareWalkForwardSleeve(
                        sessionId,
                        strategyName,
                        symbol,
                        session.Interval,
                        staticParameters[symbol], // The JSON parameters
                        session.InitialCapital,
                        oosMarketData.Where(p => p.Symbol == symbol).ToList(), // Pass in the pre-fetched data
                        false // TODO: session.UseMocExit (session object needs this)
                    );

                    if (sleeve != null)
                    {
                        // Hydrate the sleeve's position state from the portfolio manager
                        sleeve.CurrentPosition = _portfolioManager.GetOpenPosition(sleeve.Symbol, sleeve.Interval);
                        if (sleeve.CurrentPosition != null)
                        {
                            // If we're holding a position from a previous cycle, load its trade record
                            // ** ERROR FIX: This method needs to be added to ITradeService **
                            sleeve.ActiveTrade = await _tradesService.GetActiveTradeForPositionAsync(sleeve.CurrentPosition.Id);
                        }
                        sleeves.Add(sleeve);
                    }
                }

                if (!sleeves.Any())
                {
                    _logger.LogWarning("No valid strategy sleeves could be prepared for SessionId: {SessionId}. Skipping cycle.", sessionId);
                    return; // The 'finally' block will still run
                }

                // 4. Create Master Timeline (FIXED: Using BacktestSleeve)
                var masterTimeline = sleeves
                    .SelectMany(s => s.PriceData.Select(p => p.Timestamp)) // 's' is BacktestSleeve
                    .Distinct()
                    .OrderBy(t => t)
                    .ToList();

                _logger.LogInformation("[SessionId: {SessionId}] Master timeline created with {Count} timestamps.", sessionId, masterTimeline.Count);
                await _tradesService.UpdateBacktestRunStatusAsync(cycleRunId, "Running");

                // 5. Run Time-Driven Loop (Copied from your old method)
                foreach (var timestamp in masterTimeline)
                {
                    if (timestamp > currentOosEndDate) break;

                    var currentPrices = new Dictionary<string, HistoricalPriceModel>();
                    foreach (var sleeve in sleeves)
                    {
                        if (sleeve.TimestampIndexMap.TryGetValue(timestamp, out var index))
                            currentPrices[sleeve.Symbol] = sleeve.PriceData[index];
                    }

                    // ** ERROR FIX: This method needs to be added to IPortfolioManager **
                    await _portfolioManager.UpdateMarketPricesAsync(timestamp, currentPrices);

                    // --- EXIT LOGIC ---
                    foreach (var sleeve in sleeves.Where(s => s.CurrentPosition != null))
                    {
                        if (sleeve.TimestampIndexMap.TryGetValue(timestamp, out var globalIndex))
                        {
                            var currentBar = sleeve.PriceData[globalIndex];
                            var dataWindow = sleeve.PriceData.Take(globalIndex + 1).ToList();
                            var indicators = GetIndicatorsForBar(sleeve.Indicators, globalIndex);

                            string exitReason = sleeve.Strategy.GetExitReason(sleeve.CurrentPosition, in currentBar, dataWindow, indicators);

                            // bool useMocExit = false; // TODO: Get from session
                            // if (useMocExit && sleeve.MocExitTimestamps.Contains(timestamp) && exitReason == "Hold")
                            //     exitReason = "Market on Close";

                            if (exitReason != "Hold")
                            {
                                var closedTrade = await ClosePositionAsync(sleeve, currentBar, cycleRunId, exitReason, indicators);
                                if (closedTrade != null) allTrades.Add(closedTrade);
                            }
                        }
                    }

                    // --- ENTRY LOGIC ---
                    foreach (var sleeve in sleeves.Where(s => s.CurrentPosition == null))
                    {
                        if (sleeve.TimestampIndexMap.TryGetValue(timestamp, out var globalIndex))
                        {
                            var currentBar = sleeve.PriceData[globalIndex];
                            marketHealthCache.TryGetValue(currentBar.Timestamp.Date, out var marketHealth);
                            var dataWindow = sleeve.PriceData.Take(globalIndex + 1).ToList();
                            var indicators = GetIndicatorsForBar(sleeve.Indicators, globalIndex);

                            var signal = sleeve.Strategy.GenerateSignal(in currentBar, null, dataWindow, indicators, marketHealth);
                            if (signal != SignalDecision.Hold)
                            {
                                await TryOpenPositionAsync(sleeve, currentBar, signal, cycleRunId, dataWindow, indicators, marketHealth, session.InitialCapital);
                            }
                        }
                    }
                } // --- End of Timeline Loop ---

                _logger.LogInformation("[SessionId: {SessionId}] Main timeline loop complete. Closing EOC positions.", sessionId);

                // 6. End of Cycle: Close remaining positions
                foreach (var sleeve in sleeves.Where(s => s.CurrentPosition != null))
                {
                    var lastBar = sleeve.PriceData.LastOrDefault(b => b.Timestamp <= currentOosEndDate) ?? sleeve.PriceData.Last();
                    var lastBarIndex = sleeve.TimestampIndexMap[lastBar.Timestamp];
                    var lastIndicators = GetIndicatorsForBar(sleeve.Indicators, lastBarIndex);
                    var closedTrade = await ClosePositionAsync(sleeve, lastBar, cycleRunId, "End of Cycle", lastIndicators);
                    if (closedTrade != null) allTrades.Add(closedTrade);
                }

                // 7. Save Cycle Results
                var cycleResult = new BacktestResult
                {
                    Trades = allTrades.ToList(),
                    TotalTrades = allTrades.Count,
                };

                await _performanceCalculator.CalculatePerformanceMetrics(cycleResult, _portfolioManager.GetTotalEquity());

                // ** ERROR FIX: These methods need to be added to your interfaces **
                var finalState = _portfolioManager.GetPortfolioState();
                await _walkForwardRepository.SaveCycleResultsAsync(sessionId, cycleRunId, cycleResult, finalState);

                await _tradesService.UpdateBacktestRunStatusAsync(cycleRunId, "Completed");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "BacktestRunner FAILED for Session {SessionId}, Cycle {Start}",
                    sessionId, currentOosStartDate.ToShortDateString());
                await _tradesService.UpdateBacktestRunStatusAsync(cycleRunId, "Failed", ex.Message);
                throw; // Re-throw to let Hangfire handle the retry
            }
            finally
            {
                // 8. THE RECURSIVE LOOP: Enqueue the *next* cycle
                var nextCycleStartDate = currentOosEndDate.AddDays(1);

                _logger.LogInformation("Enqueuing NEXT cycle for Session {SessionId} starting {StartDate}",
                    sessionId, nextCycleStartDate.ToShortDateString());

                _backgroundJobClient.Enqueue<IPortfolioBacktestOrchestrator>(o =>
                    o.ExecuteCycle(sessionId, nextCycleStartDate, totalEndDate));
            }
        }

        private async Task<BacktestSleeve?> PrepareWalkForwardSleeve(
            Guid sessionId, string strategyName, string symbol, string interval,
            string parametersJson, double initialCapital,
            List<HistoricalPriceModel> priceData, bool useMocExit)
        {
            var parameters = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson);

            var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(
                strategyName,
                initialCapital,
                parameters
            );

            if (strategyInstance == null)
            {
                _logger.LogError("[SessionId: {SessionId}] Failed to create strategy instance '{StrategyName}' for {Symbol}.", sessionId, strategyName, symbol);
                return null;
            }

            var orderedData = priceData.OrderBy(p => p.Timestamp).ToList();
            int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();

            if (orderedData.Count < strategyMinimumLookback)
            {
                _logger.LogWarning("[SessionId: {SessionId}] Not enough OOS data for {Symbol} ({Count} bars) to meet lookback {Lookback}. Skipping.",
                    sessionId, symbol, orderedData.Count, strategyMinimumLookback);
                return null;
            }

            _logger.LogDebug("[SessionId: {SessionId}] Loaded {Count} bars for {Symbol}. Pre-calculating indicators...", sessionId, orderedData.Count, symbol);

            var highPrices = orderedData.Select(p => p.HighPrice).ToArray();
            var lowPrices = orderedData.Select(p => p.LowPrice).ToArray();
            var closePrices = orderedData.Select(p => p.ClosePrice).ToArray();

            // Porting your exact indicator logic
            var atrPeriod = ParameterHelper.GetParameterOrDefault(parameters, "AtrPeriod", 14);
            var stdDevMultiplier = strategyInstance.GetStdDevMultiplier(); // This should be from params

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

            // ** ERROR FIX: Create the TimestampIndexMap and MocExitTimestamps here **
            var timestampIndexMap = orderedData
                .Select((data, index) => new { data.Timestamp, index })
                .ToDictionary(x => x.Timestamp, x => x.index);

            var mocExitTimestamps = useMocExit
                ? orderedData.GroupBy(p => p.Timestamp.Date).Select(g => g.Max(p => p.Timestamp)).ToHashSet()
                : new HashSet<DateTime>();

            _logger.LogInformation("[SessionId: {SessionId}] Sleeve for {Symbol} prepared successfully.", sessionId, symbol);

            // ** ERROR FIX: Correct constructor call with all 7 arguments **
            return new BacktestSleeve(
                symbol, interval, strategyInstance, orderedData, indicators, timestampIndexMap, mocExitTimestamps
            );
        }


        [AutomaticRetry(Attempts = 1)]
        public async Task<BacktestResult> RunPortfolioBacktest(BacktestConfiguration config, Guid runId)
        {
            _logger.LogInformation("Starting portfolio backtest [RunId: {RunId}] for session [SessionId: {SessionId}]", runId, config.SessionId);

            if (config == null)
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Configuration error.");
                throw new ArgumentException("Could not deserialize configuration.", nameof(config));
            }

            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Initializing");
            var result = new BacktestResult();
            var allTrades = new ConcurrentBag<TradeSummary>();

            var initialPortfolioState = await _tradesService.GetLatestPortfolioStateAsync(config.SessionId.Value);

            if (initialPortfolioState.HasValue)
                _portfolioManager.HydrateState(initialPortfolioState.Value.Cash, initialPortfolioState.Value.OpenPositions);
            else
                await _portfolioManager.Initialize(config.SessionId.Value, config.InitialCapital);

            try
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Loading Data");

                var marketHealthCache = new ConcurrentDictionary<DateTime, MarketHealthScore>();
                for (var day = config.StartDate.Date; day <= config.EndDate.Date; day = day.AddDays(1))
                    marketHealthCache.TryAdd(day, await _marketHealthService.GetCurrentMarketHealth(day));

                var sleeves = new List<BacktestSleeve>();
                var testCaseStream = _securitiesOverviewService.StreamSecuritiesForBacktest(config.Symbols, config.Intervals);
                await foreach (var testCase in testCaseStream)
                {
                    var sleeve = await PrepareStrategySleeveAsync(testCase, config, runId);
                    if (sleeve == null)
                        continue;

                    sleeves.Add(sleeve);
                }

                if (!sleeves.Any())
                {
                    _logger.LogWarning("No valid strategy sleeves could be prepared for RunId: {RunId}. Aborting.", runId);
                    await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "No data for any symbols.");
                    return result;
                }

                var masterTimeline = sleeves
                    .SelectMany(s => s.PriceData.Select(p => p.Timestamp))
                    .Distinct()
                    .OrderBy(t => t)
                    .ToList();

                _logger.LogInformation("[RunId: {RunId}] Master timeline created with {Count} unique timestamps.", runId, masterTimeline.Count);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");

                foreach (var timestamp in masterTimeline)
                {
                    if (timestamp > config.EndDate) break;

                    foreach (var sleeve in sleeves.Where(s => s.CurrentPosition != null))
                    {
                        if (sleeve.TimestampIndexMap.TryGetValue(timestamp, out var globalIndex))
                        {
                            var currentBar = sleeve.PriceData[globalIndex];
                            var dataWindow = sleeve.PriceData.Take(globalIndex + 1).ToList();
                            var indicators = GetIndicatorsForBar(sleeve.Indicators, globalIndex);
                            string exitReason = sleeve.Strategy.GetExitReason(sleeve.CurrentPosition, in currentBar, dataWindow, indicators);

                            if (config.UseMocExit && sleeve.MocExitTimestamps.Contains(timestamp) && exitReason == "Hold")
                                exitReason = "Market on Close";

                            if (exitReason != "Hold")
                            {
                                var closedTrade = await ClosePositionAsync(sleeve, currentBar, runId, exitReason, indicators);
                                if (closedTrade != null) allTrades.Add(closedTrade);
                            }
                        }
                    }
                    foreach (var sleeve in sleeves.Where(s => s.CurrentPosition == null))
                    {
                        if (sleeve.TimestampIndexMap.TryGetValue(timestamp, out var globalIndex))
                        {
                            var currentBar = sleeve.PriceData[globalIndex];
                            marketHealthCache.TryGetValue(currentBar.Timestamp.Date, out var marketHealth);
                            var dataWindow = sleeve.PriceData.Take(globalIndex + 1).ToList();
                            var indicators = GetIndicatorsForBar(sleeve.Indicators, globalIndex);

                            var signal = sleeve.Strategy.GenerateSignal(in currentBar, null, dataWindow, indicators, marketHealth);
                            if (signal != SignalDecision.Hold)
                            {
                                await TryOpenPositionAsync(sleeve, currentBar, signal, runId, dataWindow, indicators, marketHealth, config.InitialCapital);
                            }
                        }
                    }
                }

                _logger.LogInformation("[RunId: {RunId}] Main timeline loop complete. Closing any remaining open positions.", runId);

                foreach (var sleeve in sleeves.Where(s => s.CurrentPosition != null))
                {
                    var lastBar = sleeve.PriceData.LastOrDefault(b => b.Timestamp <= config.EndDate) ?? sleeve.PriceData.Last();
                    var lastBarIndex = sleeve.TimestampIndexMap[lastBar.Timestamp];
                    var lastIndicators = GetIndicatorsForBar(sleeve.Indicators, lastBarIndex);
                    var closedTrade = await ClosePositionAsync(sleeve, lastBar, runId, "End of Backtest", lastIndicators);
                    if (closedTrade != null) allTrades.Add(closedTrade);
                }

                result.Trades.AddRange(allTrades);
                result.TotalTrades = result.Trades.Count;
                result.OptimizationResultId = config.OptimizationResultId;

                await _performanceCalculator.CalculatePerformanceMetrics(result, config.InitialCapital);
                await _tradesService.UpdateBacktestPerformanceMetrics(runId, result, config.InitialCapital);
                //await _tradesService.UpdateBacktestRunStatusAsync(runId, "Completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during portfolio backtest [RunId: {RunId}]", runId);
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", ex.Message);
                throw;
            }

            return result;
        }

        private async Task TryOpenPositionAsync(BacktestSleeve sleeve, HistoricalPriceModel currentBar, SignalDecision signal, Guid runId,
            List<HistoricalPriceModel> dataWindow, Dictionary<string, double> indicators, MarketHealthScore? marketHealth, double initialCapital)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var liquidityService = scope.ServiceProvider.GetRequiredService<ILiquidityService>();
            var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();
            var tradesService = scope.ServiceProvider.GetRequiredService<ITradeService>();

            long minimumAdv = sleeve.Strategy.GetMinimumAverageDailyVolume();
            if (!liquidityService.IsSymbolLiquidAtTime(sleeve.Symbol, sleeve.Interval, minimumAdv, currentBar.Timestamp, 20, sleeve.PriceData))
            {
                _logger.LogDebug("[RunId: {RunId}] Signal generated for {Symbol} but skipped due to low liquidity.", runId, sleeve.Symbol);
                return;
            }

            double allocationAmount = sleeve.Strategy.GetAllocationAmount(
                in currentBar,
                dataWindow,
                indicators,
                initialCapital * 0.02,
                _portfolioManager.GetTotalEquity(),
                0.5,
                1,
                marketHealth.Value
            );

            if (allocationAmount <= 0) return;

            double rawEntryPrice = currentBar.ClosePrice;
            int quantity = (int)Math.Floor(allocationAmount / rawEntryPrice);
            if (quantity <= 0) return;

            var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;
            double totalEntryCost = await transactionCostService.GetSpreadCost(rawEntryPrice, quantity, sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);
            double totalCashOutlay = (quantity * rawEntryPrice) + totalEntryCost;
            double commissionCost = await transactionCostService.CalculateCommissionCost(rawEntryPrice, quantity, sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);
            double slippageCost = await transactionCostService.CalculateSlippageCost(rawEntryPrice, quantity, direction, sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);
            double otherTransactionCost = await transactionCostService.CalculateOtherCost(rawEntryPrice, quantity, sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);

            if (!await _portfolioManager.CanOpenPosition(totalCashOutlay))
            {
                _logger.LogWarning("[RunId: {RunId}] Insufficient capital to open {Symbol} position. Required: {Required:C}, Available: {Available:C}",
                    runId, sleeve.Symbol, totalCashOutlay, _portfolioManager.GetAvailableCapital());
                return;
            }

            var newOrExistingPosition = await _portfolioManager.OpenPosition(sleeve.Symbol,
                sleeve.Interval, direction, quantity, rawEntryPrice, currentBar.Timestamp, totalEntryCost);

            double initialMae = 0.0;
            double initialMfe = 0.0;

            if (direction == PositionDirection.Long)
            {
                initialMae = rawEntryPrice - currentBar.LowPrice;
                initialMfe = currentBar.HighPrice - rawEntryPrice;
            }
            else
            {
                initialMae = currentBar.HighPrice - rawEntryPrice;
                initialMfe = rawEntryPrice - currentBar.LowPrice;
            }

            initialMae = Math.Max(0, initialMae);
            initialMfe = Math.Max(0, initialMfe);

            var activeTrade = new TradeSummary
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                StrategyName = sleeve.Strategy.Name,
                EntryDate = currentBar.Timestamp,
                EntryPrice = rawEntryPrice,
                Direction = direction.ToString(),
                Quantity = quantity,
                Symbol = sleeve.Symbol,
                Interval = sleeve.Interval,
                TotalTransactionCost = totalEntryCost,
                CommissionCost = commissionCost,
                SlippageCost = slippageCost,
                OtherTransactionCost = otherTransactionCost,
                MaxAdverseExcursion = initialMae,
                MaxFavorableExcursion = initialMfe,
                GrossProfitLoss = 0,
                HoldingPeriodMinutes = 0,
            };

            if (newOrExistingPosition == null)
            {
                _logger.LogCritical("[RunId: {RunId}] FATAL: Position open reported success but no position returned for {Symbol}. State inconsistency detected.", runId, sleeve.Symbol);
                return;
            }


            sleeve.ActiveTrade = activeTrade;
            sleeve.CurrentPosition = newOrExistingPosition;

            double entryAtr = indicators.GetValueOrDefault("ATR", 0);
            if (entryAtr > 0)
            {
                sleeve.CurrentPosition.StopLossPrice = (direction == PositionDirection.Long)
                    ? sleeve.CurrentPosition.AverageEntryPrice - (sleeve.Strategy.GetAtrMultiplier() * entryAtr)
                    : sleeve.CurrentPosition.AverageEntryPrice + (sleeve.Strategy.GetAtrMultiplier() * entryAtr);
            }

            await tradesService.SaveOrUpdateBacktestTrade(activeTrade);
        }

        private async Task<TradeSummary?> ClosePositionAsync(StrategySleeve sleeve, HistoricalPriceModel currentBar,
            Guid runId, string exitReason, IReadOnlyDictionary<string, double> indicators)
        {
            if (sleeve.CurrentPosition == null || sleeve.ActiveTrade == null)
            {
                _logger.LogError("[RunId: {RunId}] Attempted to close a position for {Symbol} but sleeve state was invalid.", runId, sleeve.Symbol);
                return null;
            }

            await using var scope = _serviceProvider.CreateAsyncScope();
            var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();
            var tradesService = scope.ServiceProvider.GetRequiredService<ITradeService>();
            var performanceCalculator = scope.ServiceProvider.GetRequiredService<IPerformanceCalculator>();

            double rawExitPrice = currentBar.ClosePrice;
            var exitSignal = sleeve.CurrentPosition.Direction == PositionDirection.Long ? SignalDecision.Sell : SignalDecision.Buy;
            double effectiveExitPrice = await transactionCostService.CalculateExitCost(rawExitPrice, sleeve.CurrentPosition.Direction,
                sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);
            double exitTransactionCost = Math.Abs(rawExitPrice - effectiveExitPrice) * sleeve.CurrentPosition.Quantity;

            var trade = sleeve.ActiveTrade;
            trade.ExitDate = currentBar.Timestamp;
            trade.ExitPrice = rawExitPrice;
            trade.TotalTransactionCost = (trade.TotalTransactionCost ?? 0) + exitTransactionCost;

            trade.ProfitLoss = performanceCalculator.CalculateProfitLoss(trade);

            if (trade.EntryPrice != 0)
                trade.ReturnPercentage = (trade.ProfitLoss / (trade.EntryPrice * trade.Quantity)) * 100;

            await _portfolioManager.ClosePosition(
                strategyName: sleeve.Strategy.Name,
                symbol: sleeve.Symbol,
                interval: sleeve.Interval,
                direction: sleeve.CurrentPosition.Direction,
                quantity: sleeve.CurrentPosition.Quantity,
                exitPrice: rawExitPrice,
                exitDate: currentBar.Timestamp,
                transactionCost: exitTransactionCost,
                profitLoss: trade.ProfitLoss ?? 0
            );

            trade.ExitReason = exitReason;

            await tradesService.SaveOrUpdateBacktestTrade(trade);

            sleeve.CurrentPosition = null;
            sleeve.ActiveTrade = null;

            return trade;
        }

        private Dictionary<string, double> GetIndicatorsForBar(Dictionary<string, double[]> allIndicators, int globalIndex)
        {
            var values = new Dictionary<string, double>();

            foreach (var indicator in allIndicators)
                if (globalIndex < indicator.Value.Length)
                    values[indicator.Key] = indicator.Value[globalIndex];
                else
                    values[indicator.Key] = double.NaN;

            return values;
        }

        private async Task<BacktestSleeve?> PrepareStrategySleeveAsync(SymbolCoverageBacktestModel testCase, BacktestConfiguration config, Guid runId)
        {
            _logger.LogCritical("[RunId: {RunId}] Preparing sleeve for {Symbol}. CONFIG DATES ARE: Start={StartDate}, End={EndDate}",
                runId, testCase.Symbol, config.StartDate, config.EndDate);

            var symbol = testCase.Symbol.Trim();
            var interval = testCase.Interval.Trim();
            _logger.LogInformation("[RunId: {RunId}] Preparing sleeve for {Symbol} [{Interval}]...", runId, symbol, interval);

            if (config.PortfolioParameters == null || !config.PortfolioParameters.TryGetValue(symbol, out var symbolSpecificParameters))
            {
                _logger.LogWarning("[RunId: {RunId}] Parameters not found for symbol {Symbol}. Skipping this sleeve.", runId, symbol);
                return null;
            }

            await using var scope = _serviceProvider.CreateAsyncScope();
            var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(
                config.StrategyName,
                config.InitialCapital,
                symbolSpecificParameters
            );

            if (strategyInstance == null)
            {
                _logger.LogError("[RunId: {RunId}] Failed to create strategy instance '{StrategyName}' for {Symbol}.", runId, config.StrategyName, symbol);
                return null;
            }

            var allPrices = new List<HistoricalPriceModel>();
            for (var year = config.StartDate.Year; year <= config.EndDate.Year; year++)
            {
                var yearStart = new DateTime(year, 1, 1);
                var yearEnd = new DateTime(year, 12, 31);
                var effectiveStart = config.StartDate > yearStart ? config.StartDate : yearStart;
                var effectiveEnd = config.EndDate < yearEnd ? config.EndDate : yearEnd;
                if (effectiveStart > effectiveEnd) continue;

                var priceChunk = await _historicalPriceService.GetHistoricalPrices(symbol, interval, effectiveStart, effectiveEnd);
                if (priceChunk == null || !priceChunk.Any())
                {
                    _logger.LogWarning("[RunId: {RunId}] No historical prices found for {Symbol} [{Interval}] between {StartDate:yyyy-MM-dd} and {EndDate:yyyy-MM-dd}.",
                        runId, symbol, interval, effectiveStart, effectiveEnd);
                    continue;
                }
                var minDate = priceChunk.Min(p => p.Timestamp);
                var maxDate = priceChunk.Max(p => p.Timestamp);
                _logger.LogCritical("[RunId: {RunId}] For symbol {Symbol}, REQUESTED dates {start} to {end}. RECEIVED dates {min} to {max}",
                    runId, symbol, effectiveStart, effectiveEnd, minDate, maxDate);
                allPrices.AddRange(priceChunk);
            }

            var orderedData = allPrices
                .OrderBy(p => p.Timestamp)
                .ToList();

            int strategyMinimumLookback = strategyInstance.GetRequiredLookbackPeriod();
            if (orderedData.Count < strategyMinimumLookback)
            {
                _logger.LogWarning("[RunId: {RunId}] Not enough historical data for {Symbol} [{Interval}] ({Count} bars) to meet lookback requirement of {Lookback}. Skipping.",
                    runId, symbol, interval, orderedData.Count, strategyMinimumLookback);
                return null;
            }

            _logger.LogDebug("[RunId: {RunId}] Loaded {Count} bars for {Symbol} [{Interval}]. Pre-calculating indicators...", runId, orderedData.Count, symbol, interval);

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
                { "RSI", rsi },
                { "UpperBand", upperBand },
                { "LowerBand", lowerBand },
                { "ATR", atr },
                { "SMA", movingAverage }
            };

            var timestampIndexMap = orderedData
                .Select((data, index) => new { data.Timestamp, index })
                .ToDictionary(x => x.Timestamp, x => x.index);

            var mocExitTimestamps = config.UseMocExit
                ? orderedData.GroupBy(p => p.Timestamp.Date).Select(g => g.Max(p => p.Timestamp)).ToHashSet()
                : new HashSet<DateTime>();

            _logger.LogInformation("[RunId: {RunId}] Sleeve for {Symbol} [{Interval}] prepared successfully.", runId, symbol, interval);

            return new StrategySleeve(
                Symbol: symbol,
                Interval: interval,
                Strategy: strategyInstance,
                PriceData: orderedData,
                Indicators: indicators,
                TimestampIndexMap: timestampIndexMap,
                MocExitTimestamps: mocExitTimestamps
            );
        }

        private async Task<TradeSummary?> ClosePositionAsync(
           IPortfolioManager portfolioManager, ITransactionCostService transactionCostService,
           IPerformanceCalculator performanceCalculator, ITradeService tradesService,
           TradeSummary activeTrade, Position currentPosition, HistoricalPriceModel exitBar,
           string symbol, string interval, Guid runId, int rollingKellyLookbackTrades,
           ConcurrentDictionary<string, double> symbolKellyHalfFractions, string exitReason,
           Dictionary<string, double> currentIndicatorValues)
        {
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

        private async Task<TradeSummary?> ClosePositionAsync(BacktestSleeve sleeve, HistoricalPriceModel currentBar,
            Guid runId, string exitReason, IReadOnlyDictionary<string, double> indicators)
        {
            if (sleeve.CurrentPosition == null || sleeve.ActiveTrade == null)
            {
                _logger.LogError("[RunId: {RunId}] Attempted to close a position for {Symbol} but sleeve state was invalid.", runId, sleeve.Symbol);
                return null;
            }

            await using var scope = _serviceProvider.CreateAsyncScope();
            var transactionCostService = scope.ServiceProvider.GetRequiredService<ITransactionCostService>();
            var tradesService = scope.ServiceProvider.GetRequiredService<ITradeService>();
            var performanceCalculator = scope.ServiceProvider.GetRequiredService<IPerformanceCalculator>();

            double rawExitPrice = currentBar.ClosePrice;
            if (exitReason.StartsWith("ATR Stop-Loss") && sleeve.CurrentPosition.StopLossPrice != null)
                rawExitPrice = sleeve.CurrentPosition.StopLossPrice;
            else if (exitReason.StartsWith("Profit Target"))
                rawExitPrice = indicators.GetValueOrDefault("SMA", currentBar.ClosePrice);

            var exitSignal = sleeve.CurrentPosition.Direction == PositionDirection.Long ? SignalDecision.Sell : SignalDecision.Buy;
            double effectiveExitPrice = await transactionCostService.CalculateExitCost(rawExitPrice, sleeve.CurrentPosition.Direction,
                sleeve.Symbol, sleeve.Interval, currentBar.Timestamp);
            double exitTransactionCost = Math.Abs(rawExitPrice - effectiveExitPrice) * sleeve.CurrentPosition.Quantity;

            var trade = sleeve.ActiveTrade;
            trade.ExitDate = currentBar.Timestamp;
            trade.ExitPrice = rawExitPrice;
            trade.TotalTransactionCost = (trade.TotalTransactionCost ?? 0) + exitTransactionCost;

            trade.ProfitLoss = performanceCalculator.CalculateProfitLoss(trade);

            if (trade.EntryPrice != 0)
                trade.ReturnPercentage = (trade.ProfitLoss / (trade.EntryPrice * trade.Quantity)) * 100;

            await _portfolioManager.ClosePosition(
                strategyName: sleeve.Strategy.Name,
                symbol: sleeve.Symbol,
                interval: sleeve.Interval,
                direction: sleeve.CurrentPosition.Direction,
                quantity: sleeve.CurrentPosition.Quantity,
                exitPrice: rawExitPrice,
                exitDate: currentBar.Timestamp,
                transactionCost: exitTransactionCost,
                profitLoss: trade.ProfitLoss ?? 0
            );

            trade.ExitReason = exitReason;
            trade.HoldingPeriodMinutes = (int)(currentBar.Timestamp - trade.EntryDate).TotalMinutes;

            await tradesService.SaveOrUpdateBacktestTrade(trade);

            sleeve.CurrentPosition = null;
            sleeve.ActiveTrade = null;

            return trade;
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
                    if (config.PortfolioParameters == null || !config.PortfolioParameters.TryGetValue(symbol, out var symbolSpecificParameters))
                    {
                        _logger.LogWarning("Parameters not found for symbol {Symbol} in SessionId {SessionId}. Skipping.", symbol, config.SessionId);
                        return;
                    }

                    await using var scope = _serviceProvider.CreateAsyncScope();

                    var strategyInstance = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(
                        config.StrategyName,
                        config.InitialCapital,
                        symbolSpecificParameters
                    );

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
                    Symbols = config.Symbols,
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

        #region
        [AutomaticRetry(Attempts = 1)]
        public async Task RunDualMomentumBacktest(string configJson, Guid runId)
        {
            var session = await _tradesService.GetSessionAsync(runId);
            var config = System.Text.Json.JsonSerializer.Deserialize<DualMomentumBacktestConfiguration>(configJson);
            if (config == null || !config.RiskAssetSymbols.Any() || string.IsNullOrWhiteSpace(config.SafeAssetSymbol))
            {
                await _tradesService.UpdateBacktestRunStatusAsync(runId, "Failed", "Invalid configuration for Dual Momentum Backtest.");
                throw new ArgumentException("Invalid configuration for Dual Momentum Backtest.");
            }

            await _tradesService.UpdateBacktestRunStatusAsync(runId, "Running");
            _logger.LogInformation("Starting Dual Momentum backtest for RunId: {RunId}", runId);

            await _portfolioManager.Initialize(session.SessionId, config.InitialCapital);
            var allTrades = new ConcurrentBag<TradeSummary>();
            var riskAssetKellyHalfFractions = new ConcurrentDictionary<string, double>();

            string strategyParametersJson = System.Text.Json.JsonSerializer.Serialize(config.StrategyParameters);
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

            await _portfolioManager.Initialize(config.SessionId.Value, config.InitialCapital);
            var allTrades = new ConcurrentBag<TradeSummary>();
            var pairKellyHalfFractions = new ConcurrentDictionary<string, double>();

            string strategyParametersJson = System.Text.Json.JsonSerializer.Serialize(config.StrategyParameters);

            Dictionary<string, object> strategyParameters = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(strategyParametersJson)
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
                    var historicalDataA = await _historicalPriceService.GetHistoricalPrices(pair.SymbolA, config.Interval, config.StartDate, config.EndDate);
                    var historicalDataB = await _historicalPriceService.GetHistoricalPrices(pair.SymbolB, config.Interval, config.StartDate, config.EndDate);

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


        #endregion
    }
}
