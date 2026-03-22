using Microsoft.AspNetCore.Routing.Matching;
using System.Text.Json;
using Temperance.Constellations.BackTesting.Interfaces;
using Temperance.Constellations.Models;
using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Models.Trading; // Assuming your Position, TradeSummary live here
using Temperance.Ephemeris.Repositories.Constellations.Implementations;
using Temperance.Ephemeris.Repositories.Constellations.Interfaces;
using Temperance.Ephemeris.Repositories.Ludus.Implementations;
using Temperance.Ephemeris.Repositories.Ludus.Interfaces;
using Temperance.Hermes.Constants;
using Temperance.Hermes.Publishing;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Implementations;
using Temperance.Services.Trading.Strategies;

namespace Temperance.Constellations.Services
{
    public class MasterBacktestRunner : IMasterBacktestRunner
    {
        private readonly IWalkForwardSessionRepository _walkForwardRepository;
        private readonly IWalkForwardSleeveRepository _walkForwardSleeveRepository;
        private readonly IPortfolioManager _portfolioManager;
        private readonly ITradeService _tradesService;
        private readonly IPerformanceCalculator _performanceCalculator;
        private readonly IShadowPerformanceRepository _performanceRepo;
        private readonly IHistoricalPriceService _priceService;
        private readonly IStrategyFactory _strategyFactory;
        private readonly ITransactionCostService _transactionCostService;
        private readonly IPortfolioCommitteeService _committee;
        private readonly IMessagePublisher _publisher;
        private readonly ILiquidityService _liquidityService;
        private readonly ICycleTrackerRepository _cycleTrackerRepository;
        private readonly IStrategyOptimizedParametersRepository _strategyOptimizedParameterRepository;
        private readonly IGpuIndicatorService _gpuIndicatorService;
        private readonly IMarketHealthService _marketHealthService;

        private readonly ILogger<MasterBacktestRunner> _logger;

        public MasterBacktestRunner(
            IWalkForwardSessionRepository walkForwardRepository,
            IWalkForwardSleeveRepository walkForwardSleeveRepository,
            IPortfolioManager portfolioManager,
            ITradeService tradesService,
            IPerformanceCalculator performanceCalculator,
            IShadowPerformanceRepository performanceRepo,
            IHistoricalPriceService priceService,
            IStrategyFactory strategyFactory,
            ITransactionCostService transactionCostService,
            IPortfolioCommitteeService committee,
            IMessagePublisher publisher,
            ILiquidityService liquidityService,
            ICycleTrackerRepository cycleTrackerRepository,
            IStrategyOptimizedParametersRepository strategyOptimizedParameterRepository,
            IGpuIndicatorService gpuIndicatorService,
            IMarketHealthService marketHealthService,
            ILogger<MasterBacktestRunner> logger)
        {
            _walkForwardRepository = walkForwardRepository;
            _walkForwardSleeveRepository = walkForwardSleeveRepository;
            _portfolioManager = portfolioManager;
            _tradesService = tradesService;
            _performanceCalculator = performanceCalculator;
            _performanceRepo = performanceRepo;
            _priceService = priceService;
            _strategyFactory = strategyFactory;
            _transactionCostService = transactionCostService;
            _committee = committee;
            _publisher = publisher;
            _liquidityService = liquidityService;
            _cycleTrackerRepository = cycleTrackerRepository;
            _strategyOptimizedParameterRepository = strategyOptimizedParameterRepository;
            _marketHealthService = marketHealthService;
            _gpuIndicatorService = gpuIndicatorService;

            _logger = logger;
        }

        public async Task ExecuteFullSessionAsync(Guid sessionId, DateTime sessionStartDate, DateTime sessionEndDate)
        {
            _logger.LogInformation("MASTER ORCHESTRATOR: Initializing 15-Year run for Session {SessionId}", sessionId);

            // 1. INITIALIZE GLOBAL STATE (Lives in RAM for the whole 15 years)
            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            await _portfolioManager.Initialize(sessionId, session.InitialCapital);

            // Dictionary mapping [Symbol] -> [List of CPOs sorted by Date]
            Dictionary<string, List<StrategyOptimizedParameterModel>> allCpoParameters = await _strategyOptimizedParameterRepository
                .GetParameterMapAsync(
                    session.StrategyName,
                    session.Interval,
                    sessionStartDate,
                    sessionEndDate
                );
            foreach (var symbolGroup in allCpoParameters.Values)
            {
                foreach (var cpo in symbolGroup)
                {
                    cpo.ParsedParmeters = JsonSerializer.Deserialize<Dictionary<string, object>>(cpo.OptimizedParametersJson);
                }
            }

            var ludusUniverse = allCpoParameters.Keys.ToList();
            _logger.LogInformation("Drafting initial roster for Cycle 1...");

            var candidates = await _committee.HoldPromotionCommitteeAsync(
                sessionId,
                session.StrategyName,
                session.Interval,
                sessionStartDate,
                50);
            var draftedSymbols = candidates.Where(c => c.IsPromoted).Select(c => c.Symbol).ToList();
            DateTime currentCycleStart = sessionStartDate;

            var lastKnownPrices = new Dictionary<string, decimal>();

            // =========================================================================
            // OUTER LOOP: THE CYCLE MANAGER (Feeds the Database)
            // =========================================================================
            while (currentCycleStart <= sessionEndDate)
            {
                DateTime currentCycleEnd = currentCycleStart.AddMonths(6).AddDays(-1);
                if (currentCycleEnd > sessionEndDate) currentCycleEnd = sessionEndDate;

                _logger.LogInformation("--- STARTING CYCLE: {Start} to {End} ---", currentCycleStart.ToShortDateString(), currentCycleEnd.ToShortDateString());

                // 1. Generate IDs for this 6-month chunk
                var portfolioRunId = Guid.NewGuid();
                var shadowRunId = Guid.NewGuid();

                // 2. Track the Cycle in the "Master Logbook"
                var cycleTracker = new CycleTrackerModel
                {
                    CycleTrackerId = Guid.NewGuid(),
                    SessionId = sessionId,
                    CycleStartDate = currentCycleStart,
                    OosStartDate = currentCycleStart,
                    OosEndDate = currentCycleEnd,
                    PortfolioBacktestRunId = portfolioRunId,
                    ShadowBacktestRunId = shadowRunId,
                    IsPortfolioBacktestComplete = false,
                    IsShadowBacktestComplete = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _cycleTrackerRepository.CreateCycle(cycleTracker);

                // 4. Initialize the "Black Box" (BacktestRun) with ALL the details
                var portfolioConfig = new BacktestConfiguration
                {

                    RunId = portfolioRunId,
                    SessionId = sessionId,
                    StrategyName = session.StrategyName,
                    Symbols = draftedSymbols,         // Now the DB knows exactly who we traded
                    Intervals = new List<string> { "60min" },
                    StartDate = currentCycleStart,
                    EndDate = currentCycleEnd,
                    InitialCapital = _portfolioManager.GetAvailableCapital(),
                };

                // This now saves the SymbolsJson and IntervalsJson properly
                await _tradesService.InitializeBacktestRunAsync(portfolioConfig, portfolioRunId);

                // 5. Signal that the engines are hot
                await _tradesService.UpdateBacktestRunStatusAsync(portfolioRunId, "Running");

                // 2. Fetch Data for this 6-Month Chunk
                var activeUniverse = await _walkForwardSleeveRepository.GetActiveSymbolsAsync(sessionId);
                var shadowUniverse = await _walkForwardSleeveRepository.GetShadowSymbolsAsync(sessionId);

                // Fetch all prices needed for this cycle for ALL symbols (Active + Shadow)
                var allSymbols = activeUniverse.Concat(shadowUniverse).Distinct().ToList();
                var indicatorCache = new Dictionary<string, Dictionary<DateTime, Dictionary<string, decimal>>>();
                var strategyPool = new Dictionary<string, ISingleAssetStrategy>();
                DateTime dataStartDate = currentCycleStart.AddDays(-60);
                var cycleMarketData = await _priceService.GetAllHistoricalPrices(allSymbols, new List<string> { session.Interval }, dataStartDate, currentCycleEnd);
                // Map data by symbol for fast O(1) lookups during the time loop
                var dataBySymbol = cycleMarketData
                    .GroupBy(p => p.Symbol)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Timestamp).ToList());

                foreach (var symbol in allSymbols)
                {
                    if (!dataBySymbol.TryGetValue(symbol, out var prices)) continue;

                    // 1. Get the CORRECT parameters for this date (Fixes the "Default Value" bug)
                    var cpoParams = GetActiveParametersForDate(allCpoParameters, symbol, currentCycleStart);

                    // 2. Create and update strategy once
                    var strategy = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(session.StrategyName, session.InitialCapital, cpoParams);
                    strategy.UpdateParameters(cpoParams); // Ensure params are pushed into the instance
                    strategyPool[symbol] = strategy;

                    // 3. GPU BATCH CALL: Calculate everything for this symbol's entire 6-month history at once
                    // This keeps your 3090 saturated and avoids the overhead of tiny calls
                    var bulkIndicators = await _gpuIndicatorService.CalculateBulkIndicatorsAsync(prices, cpoParams);
                    indicatorCache[symbol] = bulkIndicators;
                }

                var cycleTrades = new List<TradeSummary>();

                // Create Master Timeline from all available timestamps
                var masterTimeline = cycleMarketData
                    .Where(d => d.Timestamp >= currentCycleStart && d.Timestamp <= currentCycleEnd)
                    .Select(d => d.Timestamp)
                    .Distinct()
                    .OrderBy(t => t)
                    .ToList();

                DateTime? lastParameterUpdate = null;
                var currentWeeklyMetrics = new Dictionary<string, decimal>();
                // =========================================================================
                // INNER LOOP: THE TIME ENGINE (Executes the Strategy)
                // =========================================================================
                foreach (var timestamp in masterTimeline)
                {
                    if (lastParameterUpdate == null || timestamp.Date >= lastParameterUpdate.Value.AddDays(7))
                    {
                        foreach (var symbol in allSymbols)
                        {
                            // Now returns the tuple with the parsed Sharpe
                            var result = GetActiveCpoForDate(allCpoParameters, symbol, timestamp);

                            if (result.Cpo != null && strategyPool.TryGetValue(symbol, out var strategy))
                            {
                                strategy.UpdateParameters(result.Cpo.ParsedParmeters);
                                currentWeeklyMetrics[symbol] = result.Sharpe; // Store for the sizer
                            }
                        }
                        lastParameterUpdate = timestamp.Date;
                        _logger.LogInformation("--- Weekly Brain Sync: {Date} ---", timestamp.ToShortDateString());
                    }

                    var currentPrices = new Dictionary<string, PriceModel>();
                    foreach (var symbol in allSymbols)
                    {
                        if (!dataBySymbol.ContainsKey(symbol)) continue;
                        var bar = dataBySymbol[symbol].FirstOrDefault(b => b.Timestamp == timestamp);
                        if (bar != null)
                        {
                            currentPrices[symbol] = bar;
                            lastKnownPrices[symbol] = bar.ClosePrice;
                        }
                    }
                    await _portfolioManager.UpdateMarketPricesAsync(timestamp, currentPrices);

                    // A. EXIT LOGIC
                    var openPositions = _portfolioManager.GetOpenPositions().ToList();
                    foreach (var position in openPositions)
                    {
                        if (!currentPrices.TryGetValue(position.Symbol, out var currentBar)) continue;

                        // LOOKUP indicators instead of calculating them
                        if (!indicatorCache.TryGetValue(position.Symbol, out var symbolCache) ||
                            !symbolCache.TryGetValue(timestamp, out var indicators)) continue;

                        var strategy = strategyPool[position.Symbol];
                        string exitReason = strategy.GetExitReason(position, in currentBar, null, indicators);

                        if (exitReason != "Hold")
                        {
                            var closedTrade = await ClosePositionAsync(position, currentBar, portfolioRunId, strategy.Name, exitReason, session.Interval, indicators);
                            if (closedTrade != null) cycleTrades.Add(closedTrade);
                        }
                    }

                    // B. ENTRY LOGIC
                    foreach (var symbol in activeUniverse)
                    {
                        if (_portfolioManager.HasOpenPosition(symbol) || !currentPrices.TryGetValue(symbol, out var currentBar)) continue;

                        // LOOKUP indicators instead of calculating them
                        if (!indicatorCache.TryGetValue(symbol, out var symbolCache) ||
                            !symbolCache.TryGetValue(timestamp, out var indicators)) continue;

                        var strategy = strategyPool[symbol];
                        var currentMarketHealth = await _marketHealthService.GetCurrentMarketHealth(timestamp);
                        currentWeeklyMetrics.TryGetValue(symbol, out decimal weeklySharpe);
                        var signal = strategy.GenerateSignal(in currentBar, null, null, indicators, currentMarketHealth);
                        if (signal != SignalDecision.Hold)
                        {
                            await TryOpenPositionAsync(strategy, symbol, currentBar, signal, portfolioRunId,
                               session.InitialCapital, session.Interval, indicators,
                               currentMarketHealth, weeklySharpe);
                        }
                    }
                }
                // =========================================================================
                // END INNER LOOP
                // =========================================================================

                _logger.LogInformation("Cycle Complete. Flushing {Count} trades to database...", cycleTrades.Count);

                // 3. FLUSH CYCLE TO DATABASE
                if (cycleTrades.Any()) await _tradesService.SaveTradesBulkAsync(cycleTrades);

                var cycleResult = new BacktestResult { Trades = cycleTrades, TotalTrades = cycleTrades.Count };
                await _performanceCalculator.CalculatePerformanceMetrics(cycleResult, _portfolioManager.GetTotalEquity());
                await _tradesService.UpdateBacktestPerformanceMetrics(portfolioRunId, cycleResult, session.InitialCapital);

                var finalState = _portfolioManager.GetPortfolioState(lastKnownPrices);
                await _walkForwardRepository.SaveCycleResultsAsync(sessionId, portfolioRunId, finalState);

                await _tradesService.UpdateBacktestRunStatusAsync(portfolioRunId, "Completed");
                await _cycleTrackerRepository.MarkCycleCompleteAsync(cycleTracker.CycleTrackerId);

                DateTime nextCycleStart = currentCycleEnd.AddDays(1);

                if (nextCycleStart <= sessionEndDate)
                {
                    _logger.LogInformation("Drafting roster for next cycle starting {Date}", nextCycleStart);
                    await _committee.HoldPromotionCommitteeAsync(
                        sessionId,
                        session.StrategyName,
                        session.Interval,
                        nextCycleStart,
                        50);
                }

                // 5. ADVANCE TIME
                currentCycleStart = nextCycleStart;
            }
            // =========================================================================
            // END OUTER LOOP
            // =========================================================================

            // Prepare the completion payload
            var completionPayload = new
            {
                SessionId = sessionId,
                FinalEquity = _portfolioManager.GetTotalEquity(),
                Timestamp = DateTime.UtcNow
            };

            // Publish to Conductor via Hermes (Nuncio)
            await _publisher.PublishAsync(HermesQueueNames.ConductorBacktestCompleteEvent, completionPayload);

            _logger.LogInformation("PHASE 3 COMPLETE: 15-Year Session {SessionId} results published to Conductor.", sessionId);
        }


        // =====================================================================================
        // PRIVATE EXECUTION HELPERS
        // =====================================================================================

        private async Task<TradeSummary?> TryOpenPositionAsync(
            ISingleAssetStrategy strategy,
            string symbol,
            PriceModel currentBar,
            SignalDecision signal,
            Guid runId,
            decimal initialCapital,
            string interval,
            Dictionary<string, decimal> indicators,
            MarketHealthScore healthScore,
            decimal sharpeKelly)
        {
            decimal rawEntryPrice = currentBar.ClosePrice;
            decimal atr = indicators["ATR"];

            // 1. CARVER COST FILTER (The "Is it worth it?" Check)
            bool isViable = _transactionCostService.IsTradeEconomicallyViable(
                symbol,
                rawEntryPrice,
                atr,
                signal,
                interval,
                currentBar.Timestamp);

            if (!isViable) return null; // Skip trade: High cost vs Low volatility

            // 2. KELLY/CARVER SIZING
            // 2% hard cap per trade of initial capital, but sized by volatility
            decimal maxTradeCap = initialCapital * 0.05m;
            decimal allocationAmount = strategy.GetAllocationAmount(
                in currentBar,
                null,
                indicators,
                maxTradeCap,
                _portfolioManager.GetTotalEquity(),
                sharpeKelly,
                1,
                healthScore);

            int quantity = (int)Math.Floor(allocationAmount / rawEntryPrice);
            if (quantity <= 0) return null;

            var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;

            // 3. TRANSACTION COST CALCULATION
            decimal effectiveEntryPrice = _transactionCostService.CalculateEntryCost(rawEntryPrice, signal, symbol, interval, currentBar.Timestamp);
            decimal totalEntryCost = _transactionCostService.GetSpreadCost(rawEntryPrice, quantity, symbol, interval, currentBar.Timestamp);
            decimal totalCashOutlay = (quantity * rawEntryPrice) + totalEntryCost;

            if (await _portfolioManager.CanOpenPosition(totalCashOutlay))
            {
                await _portfolioManager.OpenPosition(symbol, interval, direction, quantity, effectiveEntryPrice, currentBar.Timestamp, totalEntryCost);

                return new TradeSummary
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    StrategyName = strategy.Name,
                    EntryDate = currentBar.Timestamp,
                    EntryPrice = effectiveEntryPrice,
                    Direction = direction.ToString(),
                    Quantity = quantity,
                    Symbol = symbol,
                    Interval = interval,
                    TotalTransactionCost = totalEntryCost,
                    EntryReason = strategy.GetEntryReason(in currentBar, null, indicators)
                };
            }
            return null;
        }

        private async Task<TradeSummary?> ClosePositionAsync(
            Models.Trading.Position position,
            PriceModel currentBar,
            Guid runId,
            string strategyName,
            string exitReason,
            string interval,
            Dictionary<string, decimal> indicators)
        {
            decimal rawExitPrice = currentBar.ClosePrice;
            SignalDecision exitSignal = position.Direction == PositionDirection.Long ? SignalDecision.Sell : SignalDecision.Buy;

            // 1. Calculate Implicit Costs (Slippage) -> Alters the price
            decimal effectiveExitPrice = _transactionCostService.CalculateEntryCost(rawExitPrice, exitSignal, position.Symbol, interval, currentBar.Timestamp);

            // 2. Calculate Explicit Costs (Commissions/Spread) -> Hard cash deduction
            decimal totalExitCost = _transactionCostService.GetSpreadCost(rawExitPrice, position.Quantity, position.Symbol, interval, currentBar.Timestamp);

            // 3. Gross PnL (Using the slippage-adjusted prices)
            decimal grossPnL = position.Direction == PositionDirection.Long
                ? (effectiveExitPrice - position.AverageEntryPrice) * position.Quantity
                : (position.AverageEntryPrice - effectiveExitPrice) * position.Quantity;

            // 4. Net PnL (Deducting the combined explicit fees of both Entry and Exit)
            decimal totalTradeCosts = position.TotalEntryCost + totalExitCost;
            decimal netPnL = grossPnL - totalTradeCosts;

            // 5. Update Portfolio RAM State
            await _portfolioManager.ClosePosition(strategyName, position.Symbol, position.Interval, position.Direction, 
                position.Quantity, effectiveExitPrice, currentBar.Timestamp, totalExitCost, netPnL);

            // 6. Generate the Database Record
            return new TradeSummary
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                StrategyName = strategyName,
                EntryDate = position.InitialEntryDate,
                ExitDate = currentBar.Timestamp,
                EntryPrice = position.AverageEntryPrice,
                ExitPrice = effectiveExitPrice,
                Direction = position.Direction.ToString(),
                Quantity = position.Quantity,
                Symbol = position.Symbol,
                Interval = interval,
                GrossProfitLoss = grossPnL,
                ProfitLoss = netPnL,
                TotalTransactionCost = totalTradeCosts, 
                EntryReason = "Strategy Entry",
                ExitReason = exitReason,
                HoldingPeriodMinutes = (int)(currentBar.Timestamp - position.InitialEntryDate).TotalMinutes
            };
        }

        private Dictionary<string, object> GetActiveParametersForDate(
            Dictionary<string, List<StrategyOptimizedParameterModel>> allCpos,
            string symbol,
            DateTime date)
        {
            if (allCpos == null || !allCpos.TryGetValue(symbol, out var cpos))
                return null;

            var activeCpo = cpos
                .Where(c => c.EndDate <= date)
                .OrderByDescending(c => c.EndDate)
                .FirstOrDefault();

            if (activeCpo == null || string.IsNullOrWhiteSpace(activeCpo.OptimizedParametersJson))
                return null;

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(activeCpo.OptimizedParametersJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JSON Error for {Symbol} at {Date}", symbol, date);
                return null;
            }
        }

        private List<PriceModel> GetRollingWindow(List<PriceModel> fullHistory, DateTime currentDate, int lookback)
        {      
            // High-performance slice: Find the index of the current bar and take the preceding N bars.
            // In a production run of 15 years, you might optimize this by maintaining an 'index' counter
            // instead of searching for the timestamp every time, but this is safe and precise.
            int currentIndex = fullHistory.FindIndex(p => p.Timestamp == currentDate);
            if (currentIndex == -1) return new List<PriceModel>();

            int startIndex = Math.Max(0, currentIndex - lookback);
            int count = currentIndex - startIndex + 1;

            return fullHistory.GetRange(startIndex, count);
        }

        private Dictionary<string, decimal> CalculateIndicatorsOnTheFly(ISingleAssetStrategy strategy, List<PriceModel> dataWindow)
        {
            // We extract the raw arrays once so the GPU/Indicator service can process them in bulk.
            var closePrices = dataWindow.Select(p => p.ClosePrice).ToArray();
            var highPrices = dataWindow.Select(p => p.HighPrice).ToArray();
            var lowPrices = dataWindow.Select(p => p.LowPrice).ToArray();

            var indicators = new Dictionary<string, decimal>();

            // These should match the keys your Strategy looks for in its 'GenerateSignal' method.
            // Replace these with your actual _gpuIndicatorService calls.
            indicators["SMA"] = _gpuIndicatorService.CalculateSma(closePrices, closePrices.Length).LastOrDefault();
            var rsi = strategy.CalculateRSI(closePrices, closePrices.Length);
            indicators["RSI"] = rsi.FirstOrDefault();
            indicators["ATR"] = _gpuIndicatorService.CalculateAtr(highPrices, lowPrices, closePrices, 14).LastOrDefault();

            // Add Bollinger Bands since you have StdDevMultiplier in your strategy fields
            var sma = indicators["SMA"];
            var stdDev = _gpuIndicatorService.CalculateStdDev(closePrices, closePrices.Length).LastOrDefault();

            // You can pull the multiplier directly from the strategy if it's public, or calculate here
            indicators["UpperBand"] = sma + (2.0m * stdDev);
            indicators["LowerBand"] = sma - (2.0m * stdDev);

            return indicators;
        }

        private (StrategyOptimizedParameterModel Cpo, decimal Sharpe) GetActiveCpoForDate(
            Dictionary<string, List<StrategyOptimizedParameterModel>> allCpos,
            string symbol,
            DateTime date)
        {
            if (allCpos == null || !allCpos.TryGetValue(symbol, out var cpos))
                return (null, 0m);

            var activeCpo = cpos
                .Where(c => c.EndDate <= date)
                .OrderByDescending(c => c.EndDate)
                .FirstOrDefault();

            if (activeCpo == null) return (null, 0m);

            decimal sharpe = 0.5m; // Default to a neutral Sharpe if parsing fails

            if (!string.IsNullOrWhiteSpace(activeCpo.Metrics))
            {
                try
                {
                    using var doc = JsonDocument.Parse(activeCpo.Metrics);
                    if (doc.RootElement.TryGetProperty("SharpeRatio", out var srProp))
                    {
                        sharpe = srProp.GetDecimal();
                    }
                }
                catch
                {
                    _logger.LogWarning("Could not parse SharpeRatio for {Symbol} at {Date}. Using default 0.5.", symbol, date);
                }
            }

            return (activeCpo, sharpe);
        }
    }
}