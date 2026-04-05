using Polly;
using System.Collections.Frozen;
using System.Text.Json;
using Temperance.Constellations.BackTesting.Interfaces;
using Temperance.Constellations.Models;
using Temperance.Constellations.Models.MarketHealth;
using Temperance.Constellations.Models.Policy;
using Temperance.Constellations.Models.Trading;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Constellations.Utilities;
using Temperance.Ephemeris.Models.Constelation;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Models.Prices;
using Temperance.Ephemeris.Repositories.Constellations.Interfaces;
using Temperance.Ephemeris.Repositories.Ludus.Implementations;
using Temperance.Ephemeris.Repositories.Ludus.Interfaces;
using Temperance.Ephemeris.Services.Financials.Interfaces;
using Temperance.Hermes.Constants;
using Temperance.Hermes.Publishing;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Trading.Strategies;

namespace Temperance.Constellations.Services
{
    public class MasterBacktestRunner : IMasterBacktestRunner
    {
        private readonly IWalkForwardSessionRepository _walkForwardRepository;
        private readonly IWalkForwardSleeveRepository _walkForwardSleeveRepository;
        private readonly IPortfolioManager _portfolioManager;
        private readonly IShadowPortfolioManager _shadowPortfolioManager;
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
        private readonly ISecuritiesService _securitiesService;
        private readonly IPortfolioTelemetryRepository _portfolioTelemetryRepository;
        private readonly ISecurityMasterService _securityMasterService;

        private Dictionary<string, string> _symbolToSectorMap = new();
        private Dictionary<string, ReadOnlyMemory<PriceModel>> _globalPriceCache = new();

        private readonly ILogger<MasterBacktestRunner> _logger;

        private const int MAX_ACTIVE_PORTFOLIO_SIZE = 30;

        public MasterBacktestRunner(
            IWalkForwardSessionRepository walkForwardRepository,
            IWalkForwardSleeveRepository walkForwardSleeveRepository,
            IPortfolioManager portfolioManager,
            IShadowPortfolioManager shadowPortfolioManager,
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
            ISecuritiesService securitiesService,
            IPortfolioTelemetryRepository portfolioTelemetryRepository,
            ISecurityMasterService securityMasterService,
            ILogger<MasterBacktestRunner> logger)
        {
            _walkForwardRepository = walkForwardRepository;
            _walkForwardSleeveRepository = walkForwardSleeveRepository;
            _portfolioManager = portfolioManager;
            _shadowPortfolioManager = shadowPortfolioManager;
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
            _securitiesService = securitiesService;
            _portfolioTelemetryRepository = portfolioTelemetryRepository;
            _securityMasterService = securityMasterService;
            _logger = logger;
        }

        public async Task ExecuteFullSessionAsync(Guid sessionId, DateTime sessionStartDate, DateTime sessionEndDate)
        {
            _logger.LogInformation("MASTER ORCHESTRATOR: Initializing 15-Year run for Session {SessionId}", sessionId);

            // 1. INITIALIZE GLOBAL STATE (Lives in RAM for the whole 15 years)
            var session = await _walkForwardRepository.GetSessionAsync(sessionId);
            await _portfolioManager.Initialize(sessionId, session.InitialCapital);
            await _shadowPortfolioManager.Initialize(sessionId, 100000000m);

            //var securities = await _securitiesService.Get();
            //_symbolToSectorMap = securities
            //    .Where(s => !string.IsNullOrEmpty(s.Symbol) && s.AssetType == "Equity")
            //    .ToDictionary(s => s.Symbol!, s => s.Sector ?? "General");

            var securitiesMaster = await _securityMasterService.GetAll();
            var etfUniverse = securitiesMaster
                .Where(x => x.AssetType == "ETF")
                .Select(x => x.Symbol)
                .ToFrozenSet();

            // Dictionary mapping [Symbol] -> [List of CPOs sorted by Date]
            Dictionary<string, List<StrategyOptimizedParameterModel>> allCpoParametersRaw = await _strategyOptimizedParameterRepository
                .GetParameterMapAsync(session.StrategyName, session.Interval, sessionStartDate, sessionEndDate);

            var allCpoParameters = allCpoParametersRaw
                    .Where(x => etfUniverse.Contains(x.Key))
                    .ToDictionary();

            foreach (var symbolGroup in allCpoParameters.Values)
            {
                foreach (var cpo in symbolGroup)
                {
                    cpo.ParsedParmeters = JsonSerializer.Deserialize<Dictionary<string, object>>(cpo.OptimizedParametersJson);
                    cpo.ParsedSharpeRatio = ExtractSharpe(cpo.Metrics) ?? 0.5m;
                }
            }

            var ludusUniverse = allCpoParameters.Keys.ToList();
            _logger.LogInformation("Drafting initial roster for Cycle 1...");

            var candidates = await _committee.HoldPromotionCommitteeAsync(
                 sessionId,
                 session.StrategyName,
                 session.Interval,
                 sessionStartDate,
                 MAX_ACTIVE_PORTFOLIO_SIZE,
                 etfUniverse);
            
            var draftedSymbols = candidates.Where(c => c.IsPromoted).Select(c => c.Symbol).ToList();
            DateTime currentCycleStart = sessionStartDate;

            //// 2. Fetch Data for this 6-Month Chunk
            //var activeUniverse = await _walkForwardSleeveRepository.GetActiveSymbolsAsync(sessionId);
            //var shadowUniverse = await _walkForwardSleeveRepository.GetShadowSymbolsAsync(sessionId);

            //// Fetch all prices needed for this cycle for ALL symbols (Active + Shadow)
            //var allSymbols = activeUniverse.Concat(shadowUniverse).Distinct().ToList();

            var allSymbols = allCpoParameters.Keys.ToList();
            var lastKnownPrices = new Dictionary<string, decimal>();

            _logger.LogInformation("Warp Drive: Building 200-Day Trend Index for all symbols...");
            var globalTrendCache = new Dictionary<string, Dictionary<DateTime, decimal>>();

            DateTime absoluteDataStart = sessionStartDate.AddDays(-250);
            _logger.LogInformation("Warp Drive: Fetching 15 years of Market Data into RAM...");
            var allMarketData = await _priceService.GetAllHistoricalPrices(
                allSymbols,
                new List<string> { session.Interval },
                absoluteDataStart,
                sessionEndDate);

            _globalPriceCache = allMarketData
                .GroupBy(p => p.Symbol)
                .ToDictionary(
                    g => g.Key,
                    g => (ReadOnlyMemory<PriceModel>)g.OrderBy(p => p.Timestamp).ToArray()
                );

            _logger.LogInformation("Warp Drive: {Count} total bars loaded into RAM.", allMarketData.Count);

            foreach (var symbol in allSymbols)
            {
                if (!_globalPriceCache.TryGetValue(symbol, out var fullPrices)) continue;

                // Pass the Memory structure directly to the GPU service
                var trends = await _gpuIndicatorService.CalculateSmaOnlyAsync(fullPrices, 1300);
                globalTrendCache[symbol] = trends;
            }

            Guid lastActiveRunId = Guid.Empty;

            // =========================================================================
            // OUTER LOOP: THE CYCLE MANAGER (Feeds the Database)
            // =========================================================================
            while (currentCycleStart <= sessionEndDate)
            {
                DateTime currentCycleEnd = currentCycleStart.AddMonths(6).AddDays(-1);
                if (currentCycleEnd > sessionEndDate) currentCycleEnd = sessionEndDate;
                if (currentCycleStart >= currentCycleEnd) break;

                _logger.LogInformation("--- STARTING CYCLE: {Start} to {End} ---", currentCycleStart.ToShortDateString(), currentCycleEnd.ToShortDateString());
                // ++++++++++++++++++ PASTE THIS NEW BLOCK +++++++++++++++++++++++
                // 1. DYNAMIC ROSTER REFRESH (The Darwinian Update)
                // Ask the DB: "Who did the Committee draft for THIS SPECIFIC cycle date?"
                var activeTask = _walkForwardSleeveRepository.GetActiveSymbolsAsync(sessionId, currentCycleStart);
                var shadowTask = _walkForwardSleeveRepository.GetShadowSymbolsAsync(sessionId, currentCycleStart);

                await Task.WhenAll(activeTask, shadowTask);

                var activeUniverse = activeTask.Result.Distinct().ToList();
                var shadowUniverse = shadowTask.Result.Distinct().ToList();

                var cyclePrepSymbols = activeUniverse.Concat(shadowUniverse).Distinct().ToList();
                // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

                DateTime dataStartDate = currentCycleStart.AddDays(-60);

                // 1. Generate IDs for this 6-month chunk
                var portfolioRunId = Guid.NewGuid();
                lastActiveRunId = portfolioRunId;
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

                await _tradesService.InitializeBacktestRunAsync(portfolioConfig, portfolioRunId);
                await _tradesService.UpdateBacktestRunStatusAsync(portfolioRunId, "Running");

                var cyclePrepList = cyclePrepSymbols.AsParallel()
                    .Where(symbol => _globalPriceCache.ContainsKey(symbol))
                    .Select(symbol =>
                    {
                        var fullPrices = _globalPriceCache[symbol];
                        var slice = GetTimeSlice(fullPrices, currentCycleStart, currentCycleEnd);
                        var cpoParams = GetActiveParametersForDate(allCpoParameters, symbol, currentCycleStart);
                        if (cpoParams == null)
                            return null;
                        var strategy = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(session.StrategyName, session.InitialCapital, cpoParams);
                        return new
                        {
                            Symbol = symbol,
                            Slice = slice,
                            Parameters = cpoParams,
                            Strategy = strategy,
                        };
                    }).ToList();

                var returnSeries = new List<decimal[]>();
                foreach (var symbol in cyclePrepSymbols)
                {
                    if (_globalPriceCache.TryGetValue(symbol, out var fullPrices))
                    {
                        var historicalSlice = GetTimeSlice(fullPrices, currentCycleStart.AddMonths(-6), currentCycleStart);
                        // Convert prices to a simple array of percentage returns
                        var returns = historicalSlice.Span.ToArray()
                            .Skip(1)
                            .Select((p, i) => (p.ClosePrice - historicalSlice.Span[i].ClosePrice) / historicalSlice.Span[i].ClosePrice)
                            .ToArray();

                        if (returns.Length > 30) returnSeries.Add(returns);
                    }
                }

                decimal currentCycleIdm = PortfolioMath.CalculateDynamicIdm(returnSeries);
                int activePortfolioSize = cyclePrepSymbols.Count > 0 ? cyclePrepSymbols.Count : 20;

                var validPreps = cyclePrepList.Where(x => x != null).ToList();
                var cycleSlices = validPreps.ToDictionary(x => x.Symbol, x => x.Slice);
                var strategyPool = validPreps.ToDictionary(x => x.Symbol, x => x.Strategy);

                var indicatorTasks = cyclePrepList.Select(async prep =>
                {
                    var cache = await _gpuIndicatorService.CalculateBulkIndicatorsAsync(prep.Slice, prep.Parameters);
                    return new { prep.Symbol, Cache = cache };
                });

                var indicatorResults = await Task.WhenAll(indicatorTasks);
                var indicatorCache = indicatorResults.ToDictionary(x => x.Symbol, x => x.Cache);

                var cycleTrades = new List<TradeSummary>();
                var cycleTelemetry = new List<PortfolioTelemetryModel>();

                // HIGHSPEED FIX 3: Fast HashSet Timeline Generation
                var masterTimelineSet = new HashSet<DateTime>();
                foreach (var slice in cycleSlices.Values)
                {
                    var span = slice.Span;
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (span[i].Timestamp >= currentCycleStart && span[i].Timestamp <= currentCycleEnd)
                        {
                            masterTimelineSet.Add(span[i].Timestamp);
                        }
                    }
                }
                var masterTimeline = masterTimelineSet.OrderBy(t => t).ToList();

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
                                currentWeeklyMetrics[symbol] = result.SharpeRatio; // Store for the sizer
                            }
                        }
                        lastParameterUpdate = timestamp.Date;
                        _logger.LogInformation("--- Weekly Brain Sync: {Date} ---", timestamp.ToShortDateString());
                    }

                    var currentPrices = allSymbols.AsParallel()
                        .Select(symbol =>
                        {
                            cycleSlices.TryGetValue(symbol, out var slice);
                            return new { Symbol = symbol, Bar = FindBarAtTimestamp(slice.Span, timestamp) };
                        })
                        .Where(x => x.Bar != null)
                        .ToDictionary(x => x.Symbol, x => x.Bar);

                    foreach (var kvp in currentPrices)
                    {
                        lastKnownPrices[kvp.Key] = kvp.Value.ClosePrice;
                    }

                    await _portfolioManager.UpdateMarketPricesAsync(timestamp, currentPrices);

                    var executionRealms = new[]
                    {
                        new { Manager = (IPortfolioManager)_portfolioManager, Universe = activeUniverse, RunId = portfolioRunId, IsShadow = false },
                        new { Manager = (IPortfolioManager)_shadowPortfolioManager, Universe = shadowUniverse, RunId = shadowRunId, IsShadow = true }
                    };

                    foreach (var realm in executionRealms)
                    {
                        var openPositions = realm.Manager.GetOpenPositions();

                        /* --- DISABLED FOR NAKED BASELINE ---
                        var sectorVolatilityMap = ... (Calculates sector limits)
                        decimal totalDailyVolTarget = (realm.Manager.GetTotalEquity() * 0.20m) / 16.0m;
                        decimal maxSectorVol = totalDailyVolTarget * TradingEnginePolicy.SECTOR_VOLATILITY_MAX_EXPOSURE_RATIO;
                        -------------------------------------- */

                        var symbolsExitedThisBar = new HashSet<string>();
                        var currentMarketHealth = await _marketHealthService.GetCurrentMarketHealth(timestamp);

                        // ==============================================================
                        // A. EXIT LOGIC (Parallel Evaluation)
                        // ==============================================================
                        var exitCandidates = openPositions.AsParallel()
                            .Select(position =>
                            {
                                if (!currentPrices.TryGetValue(position.Symbol, out var currentBar)) return null;
                                if (!indicatorCache.TryGetValue(position.Symbol, out var symbolCache) || !symbolCache.TryGetValue(timestamp, out var indicators)) return null;

                                // UPDATE JOURNEY METRICS (Safe: 1 thread per unique position object)
                                if (position.HighestPriceSinceEntry == 0) position.HighestPriceSinceEntry = position.AverageEntryPrice;
                                if (position.LowestPriceSinceEntry == 0) position.LowestPriceSinceEntry = position.AverageEntryPrice;
                                if (currentBar.HighPrice > position.HighestPriceSinceEntry) position.HighestPriceSinceEntry = currentBar.HighPrice;
                                if (currentBar.LowPrice < position.LowestPriceSinceEntry) position.LowestPriceSinceEntry = currentBar.LowPrice;
                                position.BarsHeld++;

                                string exitReason = "Hold";

                                // REINSTATEMENT 3: The Dynamic Multi-Tier Trailing Ratchet
                                if (exitReason == "Hold" && position.BarsHeld > 2)
                                {
                                    decimal atr = indicators.TryGetValue("ATR", out var a) ? a : 0m;
                                    if (atr > 0)
                                    {
                                        if (position.Direction == PositionDirection.Long)
                                        {
                                            decimal mfeInAtr = (position.HighestPriceSinceEntry - position.AverageEntryPrice) / atr;

                                            if (mfeInAtr >= 3.0m) // TIER 3: The Parabolic Choke
                                            {
                                                decimal parabolicFloor = position.HighestPriceSinceEntry - (atr * 0.3m);
                                                if (currentBar.ClosePrice <= parabolicFloor) exitReason = "Trailing Ratchet (Parabolic Secured)";
                                            }
                                            else if (mfeInAtr >= 2.0m) // TIER 2: The Trend Trail
                                            {
                                                decimal trailingFloor = position.HighestPriceSinceEntry - (atr * 1.0m);
                                                if (currentBar.ClosePrice <= trailingFloor) exitReason = "Trailing Ratchet (Peak Trail Secured)";
                                            }
                                            else if (mfeInAtr >= 1.0m) // TIER 1: The Break-Even Protector
                                            {
                                                decimal breakEvenFloor = position.AverageEntryPrice + (atr * 0.4m);
                                                if (currentBar.ClosePrice <= breakEvenFloor) exitReason = "Trailing Ratchet (Break-Even Secured)";
                                            }
                                        }
                                        else if (position.Direction == PositionDirection.Short)
                                        {
                                            decimal mfeInAtr = (position.AverageEntryPrice - position.LowestPriceSinceEntry) / atr;

                                            if (mfeInAtr >= 3.0m) // TIER 3: The Parabolic Choke
                                            {
                                                decimal parabolicCeiling = position.LowestPriceSinceEntry + (atr * 0.3m);
                                                if (currentBar.ClosePrice >= parabolicCeiling) exitReason = "Trailing Ratchet (Parabolic Secured)";
                                            }
                                            else if (mfeInAtr >= 2.0m) // TIER 2: The Trend Trail
                                            {
                                                decimal trailingCeiling = position.LowestPriceSinceEntry + (atr * 1.0m);
                                                if (currentBar.ClosePrice >= trailingCeiling) exitReason = "Trailing Ratchet (Peak Trail Secured)";
                                            }
                                            else if (mfeInAtr >= 1.0m) // TIER 1: The Break-Even Protector
                                            {
                                                decimal breakEvenCeiling = position.AverageEntryPrice - (atr * 0.4m);
                                                if (currentBar.ClosePrice >= breakEvenCeiling) exitReason = "Trailing Ratchet (Break-Even Secured)";
                                            }
                                        }
                                    }
                                }

                                bool isPartial = false;

                                if (exitReason == "Hold")
                                {
                                    var strategy = strategyPool[position.Symbol];

                                    //// Check for the SMA Partial Scalpel FIRST
                                    //if (strategy.ShouldTakePartialProfit(position, in currentBar, indicators))
                                    //{
                                    //    exitReason = "SMA Partial Profit (50%)";
                                    //    isPartial = true;
                                    //}
                                    //else
                                    //{
                                    //    string proposedExit = strategy.GetExitReason(position, in currentBar, null, indicators);
                                    //    if (proposedExit != "Hold") exitReason = proposedExit;
                                    //}

                                    string proposedExit = strategy.GetExitReason(position, in currentBar, null, indicators);
                                    if (proposedExit != "Hold") exitReason = proposedExit;
                                }

                                // If nothing triggered, drop it from the pipeline
                                if (exitReason == "Hold") return null;

                                return new
                                {
                                    Position = position,
                                    Bar = currentBar,
                                    Indicators = indicators,
                                    ExitReason = exitReason,
                                    StrategyName = strategyPool[position.Symbol].Name,
                                    IsPartial = false // <--- PASS THE FLAG TO THE EXECUTION LOOP
                                };
                            })
                            .Where(result => result != null)
                            .ToList();

                        // ==============================================================
                        // A.2 EXIT EXECUTION (Sequential State Mutation)
                        // ==============================================================
                        foreach (var exit in exitCandidates)
                        {
                            // Ensure we haven't already processed an exit for this symbol on this bar
                            if (!symbolsExitedThisBar.Contains(exit.Position.Symbol))
                            {
                                // Determine the slice size. If it's a partial, cut it in half.
                                int? sliceQty = exit.IsPartial ? (int)Math.Floor(exit.Position.Quantity * 0.2) : null;

                                // Safety check: Don't execute a partial if the math resulted in 0 shares
                                if (exit.IsPartial && sliceQty <= 0) continue;
                                var closedTrade = await ClosePositionAsync(
                                    realm.Manager,
                                    exit.Position,
                                    exit.Bar,
                                    realm.RunId,
                                    exit.StrategyName,
                                    exit.ExitReason,
                                    session.Interval,
                                    exit.Indicators,
                                    sliceQty);

                                if (closedTrade != null)
                                {
                                    cycleTrades.Add(closedTrade);
                                    symbolsExitedThisBar.Add(exit.Position.Symbol);
                                }
                            }
                        }


                        // B. ENTRY LOGIC
                        var entryCandidates = realm.Universe.AsParallel()
                            .Where(symbol => !symbolsExitedThisBar.Contains(symbol) &&
                                             !_portfolioManager.HasOpenPosition(symbol) &&
                                             currentPrices.ContainsKey(symbol))
                            .Select(symbol =>
                            {
                                var currentBar = currentPrices[symbol];
                                string sector = GetSectorForSymbol(symbol);

                                if (!indicatorCache.TryGetValue(symbol, out var cache) || !cache.TryGetValue(timestamp, out var indicators))
                                    return null;

                                if (globalTrendCache.TryGetValue(symbol, out var trends) && trends.TryGetValue(timestamp, out var trendVal))
                                    indicators["SMA_Long"] = trendVal;

                                var strategy = strategyPool[symbol];

                                // PURE LUDUS ENTRY SIGNAL
                                var signal = strategy.GenerateSignal(in currentBar, null, null, indicators, currentMarketHealth.OverallRegime);

                                if (signal == SignalDecision.Hold) return null;

                                // Get the base historical expectation
                                currentWeeklyMetrics.TryGetValue(symbol, out decimal baseSharpe);

                                // Safety check: if Sharpe is negative or 0, give it a baseline so the multiplier works
                                decimal safeSharpe = Math.Max(baseSharpe, 0.1m);

                                // ====================================================================
                                // CALCULATE DYNAMIC DISLOCATION SCORE (CONVICTION MULTIPLIER)
                                // ====================================================================
                                decimal convictionMultiplier = 1.0m;

                                indicators.TryGetValue("RSI", out decimal rsi);
                                indicators.TryGetValue("ATR", out decimal atr);
                                indicators.TryGetValue("LowerBand", out decimal lowerBand);
                                indicators.TryGetValue("UpperBand", out decimal upperBand);

                                if (signal == SignalDecision.Buy)
                                {
                                    // 1. RSI Depth (assuming standard 30 threshold)
                                    if (rsi > 0 && rsi < 30m)
                                    {
                                        decimal rsiDepth = (30m - rsi) / 30m; // E.g., RSI of 15 = 0.5 depth
                                        convictionMultiplier += (rsiDepth * 1.5m); // Adds up to 1.5x multiplier
                                    }

                                    // 2. Bollinger Band Piercing Depth (Normalized by ATR)
                                    if (atr > 0 && currentBar.ClosePrice < lowerBand)
                                    {
                                        decimal bbStretch = (lowerBand - currentBar.ClosePrice) / atr;
                                        convictionMultiplier += bbStretch; // Adds 1.0x for every 1 ATR below the band
                                    }
                                }
                                else if (signal == SignalDecision.Sell)
                                {
                                    // 1. RSI Euphoria (assuming standard 70 threshold)
                                    if (rsi > 70m)
                                    {
                                        decimal rsiDepth = (rsi - 70m) / 30m;
                                        convictionMultiplier += (rsiDepth * 1.5m);
                                    }

                                    // 2. Bollinger Band Piercing Depth (Normalized by ATR)
                                    if (atr > 0 && currentBar.ClosePrice > upperBand)
                                    {
                                        decimal bbStretch = (currentBar.ClosePrice - upperBand) / atr;
                                        convictionMultiplier += bbStretch;
                                    }
                                }

                                // Cap the multiplier to prevent a single flash-crash from overflowing the scoring logic
                                convictionMultiplier = Math.Clamp(convictionMultiplier, 1.0m, 5.0m);

                                // THE FINAL SCORE: Historical Edge × Real-Time Dislocation Depth
                                decimal dynamicScore = safeSharpe * convictionMultiplier;

                                return new
                                {
                                    Symbol = symbol,
                                    Sector = sector,
                                    Strategy = strategy,
                                    Bar = currentBar,
                                    Signal = signal,
                                    Indicators = indicators,
                                    Sharpe = baseSharpe,
                                    Score = dynamicScore // <--- THE UPDATED RANKING METRIC
                                };
                            })
                            .Where(result => result != null)
                            .ToList();

                        // Now when you sort, the most violently dislocated assets float to the top
                        var rankedCandidates = entryCandidates.OrderByDescending(c => c.Score).ToList();

                        decimal currentEquitySnapshot = realm.Manager.GetTotalEquity();
                        _logger.LogInformation("Cycle {CycleStart} | Realm: {Realm} | Timestamp: {Timestamp} | Equity Snapshot: {Equity:C2} | Candidates: {CandidateCount}",
                            currentCycleStart.ToShortDateString(),
                            realm.IsShadow ? "Shadow" : "Real",
                            timestamp,
                            currentEquitySnapshot,
                            rankedCandidates.Count);

                        decimal globalMarginCeiling = currentEquitySnapshot * 1.50m;

                        // Calculate the buffer fresh for EVERY realm loop
                        decimal liveExposureBuffer = realm.Manager.GetAllocatedCapital();

                        foreach (var candidate in rankedCandidates)
                        {
                            /* --- DISABLED: SECTOR VOLATILITY EXECUTION CHECK ---
                            sectorVolatilityMap.TryGetValue(candidate.Sector, out decimal currentSectorVol);
                            if (currentSectorVol >= maxSectorVol) continue;
                            ------------------------------------------------------ */

                            if (!realm.IsShadow)
                            {
                                // 1. THE BOUNCER: Check the live buffer before even knocking on the strategy door
                                if (liveExposureBuffer >= globalMarginCeiling)
                                {
                                    _logger.LogWarning("GLOBAL MARGIN CAP HIT: Rejecting {Symbol} for this bar.", candidate.Symbol);
                                    continue;
                                }

                                var trade = await TryOpenPositionAsync(
                                    realm.Manager,
                                    candidate.Strategy,
                                    candidate.Symbol,
                                    candidate.Bar,
                                    candidate.Signal,
                                    realm.RunId,
                                    session.InitialCapital,
                                    session.Interval,
                                    candidate.Indicators,
                                    currentMarketHealth.RawMacroScore,
                                    candidate.Sharpe,
                                    currentCycleIdm,
                                    activePortfolioSize,
                                    liveExposureBuffer);

                                if (trade != null)
                                {
                                    // Update the live buffer so the math tracks, even if we ignore the ceiling
                                    liveExposureBuffer += (trade.Quantity * trade.EntryPrice);

                                    // cycleTrades.Add(trade); <-- Keep disabled (Ghost Trade Fix)
                                }
                            }
                            else
                            {
                                // =====================================================================
                                // SHADOW REALM: The "Infinite Money" control group. 
                                // We pass 0m for exposure so it NEVER triggers the Margin Governor.
                                // =====================================================================
                                var shadowTrade = await TryOpenPositionAsync(
                                    realm.Manager,
                                    candidate.Strategy,
                                    candidate.Symbol,
                                    candidate.Bar,
                                    candidate.Signal,
                                    realm.RunId,
                                    session.InitialCapital,
                                    session.Interval,
                                    candidate.Indicators,
                                    currentMarketHealth.RawMacroScore,
                                    candidate.Sharpe,
                                    currentCycleIdm,
                                    activePortfolioSize,
                                    0m); // 0m exposure = No ceiling

                                //if (shadowTrade != null)
                                //{
                                //    cycleTrades.Add(shadowTrade);
                                //}
                            }
                        }
                    }

                    var liveState = _portfolioManager.GetPortfolioState(currentPrices);
                    decimal liveAllocated = _portfolioManager.GetAllocatedCapital();

                    // Calculate Leverage: Deployed / Total Equity
                    decimal activeLeverage = liveState.TotalEquity > 0
                        ? (liveAllocated / liveState.TotalEquity)
                        : 0m;

                    cycleTelemetry.Add(new PortfolioTelemetryModel
                    {
                        RunId = portfolioRunId,
                        Timestamp = timestamp,
                        Cash = liveState.Cash ?? 0m,
                        AllocatedCapital = liveAllocated,
                        UnrealizedPnL = liveState.UnrealizedPnL ?? 0m,
                        TotalEquity = liveState.TotalEquity,
                        ActiveLeverage = activeLeverage
                    });
                }

                // =========================================================================
                // END INNER LOOP
                // =========================================================================

                _logger.LogInformation("Cycle Complete. Calculating End-of-Cycle State...");

                // 1. SPLIT THE DATASTREAMS!
                var realTrades = cycleTrades.Where(t => t.RunId == portfolioRunId).ToList();
                var shadowTrades = cycleTrades.Where(t => t.RunId == shadowRunId).ToList();

                // 2. CALCULATE REAL CYCLE-END UTILIZATION
                var openPositionsAtEnd = _portfolioManager.GetOpenPositions().ToList();
                decimal totalUnrealizedPnL = 0m;
                decimal totalAllocatedCapital = 0m;

                foreach (var pos in openPositionsAtEnd)
                {
                    if (lastKnownPrices.TryGetValue(pos.Symbol, out decimal lastPrice))
                    {
                        decimal grossPnL = pos.Direction == PositionDirection.Long
                            ? (lastPrice - pos.AverageEntryPrice) * pos.Quantity
                            : (pos.AverageEntryPrice - lastPrice) * pos.Quantity;

                        totalUnrealizedPnL += grossPnL;
                        totalAllocatedCapital += (pos.Quantity * pos.AverageEntryPrice);
                    }
                }

                decimal availableCash = _portfolioManager.GetAvailableCapital();

                _logger.LogInformation("Cycle End Cash: {Cash:C2} | Allocated: {Allocated:C2} | Unrealized PnL: {Unrealized:C2}",
                    availableCash, totalAllocatedCapital, totalUnrealizedPnL);

                // =========================================================================
                // 3. CONCURRENT DATA FLUSH (Trades, Telemetry, Shadow Realm)
                // =========================================================================
                var dbFlushTasks = new List<Task>();

                if (realTrades.Any())
                    dbFlushTasks.Add(_tradesService.SaveTradesBulkAsync(realTrades));

                if (cycleTelemetry.Any())
                    dbFlushTasks.Add(_portfolioTelemetryRepository.SaveTelemetryBulkAsync(cycleTelemetry));

                if (shadowTrades.Any())
                {
                    // Offload the entire shadow calculation and save process to a background task
                    dbFlushTasks.Add(Task.Run(async () =>
                    {
                        var shadowSummary = new BacktestResult
                        {
                            Trades = shadowTrades,
                            Configuration = portfolioConfig
                        };

                        var shadowComponents = await _performanceCalculator.CalculateSleevePerformanceFromTradesAsync(
                            shadowSummary,
                            sessionId,
                            shadowRunId);

                        var shadowPerformanceRecords = shadowComponents.Select(sc => new ShadowPerformanceModel
                        {
                            RunId = sc.RunId,
                            Symbol = sc.Symbol,
                            SharpeRatio = sc.SharpeRatio ?? 0m,
                            ProfitLoss = sc.ProfitLoss ?? 0m,
                            TotalTrades = sc.TotalTrades ?? 0,
                            WinRate = sc.WinRate ?? 0m
                        }).ToList();

                        await _performanceRepo.SaveShadowPerformanceBulkAsync(shadowPerformanceRecords);
                        _logger.LogInformation("Shadow Realm: Logged out-of-sample performance for {Count} benched symbols.", shadowPerformanceRecords.Count);
                    }));
                }

                // Wait for all heavy database inserts and shadow math to finish simultaneously
                await Task.WhenAll(dbFlushTasks);

                // =========================================================================
                // 4. UPDATE REAL PORTFOLIO PERFORMANCE (Sequential Math)
                // =========================================================================
                var cycleResult = new BacktestResult
                {
                    Trades = realTrades,
                    TotalTrades = realTrades.Count,
                    EndingCash = availableCash,
                    AllocatedCapital = totalAllocatedCapital,
                    UnrealizedProfitLoss = totalUnrealizedPnL,
                    OpenTradesCount = openPositionsAtEnd.Count
                };

                // Do the heavy lifting math sequentially to ensure thread safety on the portfolio state
                await _performanceCalculator.CalculatePerformanceMetrics(cycleResult, _portfolioManager.GetTotalEquity());
                var finalState = _portfolioManager.GetPortfolioState(lastKnownPrices);

                // =========================================================================
                // 5. CONCURRENT METADATA FLUSH (Status, Results, Tracker)
                // =========================================================================
                var finalCleanupTasks = new List<Task>
                {
                    _tradesService.UpdateBacktestPerformanceMetrics(portfolioRunId, cycleResult, session.InitialCapital),
                    _walkForwardRepository.SaveCycleResultsAsync(sessionId, portfolioRunId, finalState),
                    _tradesService.UpdateBacktestRunStatusAsync(portfolioRunId, "Completed"),
                    _cycleTrackerRepository.MarkCycleCompleteAsync(cycleTracker.CycleTrackerId)
                };

                // Wait for all 4 status updates to write to the DB simultaneously
                await Task.WhenAll(finalCleanupTasks);

                // =========================================================================
                // 6. PREPARE NEXT CYCLE
                // =========================================================================
                DateTime nextCycleStart = currentCycleEnd.AddDays(1);

                if (nextCycleStart <= sessionEndDate)
                {
                    _logger.LogInformation("Scouting the universe for high-volatility candidates...");

                    await _committee.HoldPromotionCommitteeAsync(
                        sessionId,
                        session.StrategyName,
                        session.Interval,
                        nextCycleStart,
                        MAX_ACTIVE_PORTFOLIO_SIZE,
                        etfUniverse);
                }

                // ADVANCE TIMEf
                currentCycleStart = nextCycleStart;
            }
            // =========================================================================
            // END OUTER LOOP
            // =========================================================================

            _logger.LogInformation("End of 15-Year Session reached. Liquidating all remaining open positions...");
            var finalOpenPositions = _portfolioManager.GetOpenPositions();
            var liquidationTrades = new List<TradeSummary>();
            foreach (var position in finalOpenPositions)
            {
                if (lastKnownPrices.TryGetValue(position.Symbol, out decimal finalPrice))
                {
                    var endOfTestBar = new PriceModel
                    {
                        Symbol = position.Symbol,
                        ClosePrice = finalPrice,
                        Timestamp = sessionEndDate
                    };

                    // We use the lastActiveRunId, not Guid.Empty!
                    var closedTrade = await ClosePositionAsync(
                        _portfolioManager,
                        position,
                        endOfTestBar,
                        lastActiveRunId, // <--- THE FIX
                        session.StrategyName ?? "Unknown",
                        "MOC Liquidation (End of Session)",
                        session.Interval,
                        new Dictionary<string, decimal>());

                    if (closedTrade != null) liquidationTrades.Add(closedTrade);
                }
            }

            if (liquidationTrades.Any())
            {
                // 1. Save the final exit trades to the DB
                await _tradesService.SaveTradesBulkAsync(liquidationTrades);
                _logger.LogInformation("Liquidated {Count} positions safely. Portfolio is 100% Cash.", liquidationTrades.Count);

                var previousTradesInFinalCycle = await _tradesService.GetTradesByRunIdAsync(lastActiveRunId);
                var allFinalCycleTrades = previousTradesInFinalCycle.ToList();
                allFinalCycleTrades.AddRange(liquidationTrades);
                var finalLiquidationResult = new BacktestResult
                {
                    Trades = allFinalCycleTrades,
                    TotalTrades = allFinalCycleTrades.Count,
                    EndingCash = _portfolioManager.GetAvailableCapital(), // Now equals Total Equity
                    AllocatedCapital = 0m,                                // 100% Cash
                    UnrealizedProfitLoss = 0m,                            // No open positions left
                    OpenTradesCount = 0
                };

                // Recalculate Sharpe/Sortino/Drawdown with the final exits included
                await _performanceCalculator.CalculatePerformanceMetrics(finalLiquidationResult, _portfolioManager.GetTotalEquity());

                // Overwrite the final cycle's DB record
                await _tradesService.UpdateBacktestPerformanceMetrics(lastActiveRunId, finalLiquidationResult, session.InitialCapital);

                // Update the global walk-forward state snapshot
                var finalState = _portfolioManager.GetPortfolioState(lastKnownPrices);
                await _walkForwardRepository.SaveCycleResultsAsync(sessionId, lastActiveRunId, finalState);
            }

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

            return;
        }


        // =====================================================================================
        // PRIVATE EXECUTION HELPERS
        // =====================================================================================
        private async Task<TradeSummary?> TryOpenPositionAsync(
            IPortfolioManager manager,
            ISingleAssetStrategy strategy,
            string symbol,
            PriceModel currentBar,
            SignalDecision signal,
            Guid runId,
            decimal initialCapital,
            string interval,
            Dictionary<string, decimal> indicators,
            int rawMacroScore,
            decimal sharpeKelly,
            decimal dynamicIdm,
            int activePortfolioSize,
            decimal currentLiveExposure) // The source of truth for this bar
        {
            decimal atr = indicators["ATR"];
            decimal lowerBand = indicators.TryGetValue("LowerBand", out var lb) ? lb : currentBar.ClosePrice;
            decimal upperBand = indicators.TryGetValue("UpperBand", out var ub) ? ub : currentBar.ClosePrice;

            // Default to the close price (Momentum Exhaustion Confirmation)
            decimal rawEntryPrice = currentBar.ClosePrice;

            // 1. CARVER COST FILTER
            bool isViable = _transactionCostService.IsTradeEconomicallyViable(
                symbol, rawEntryPrice, atr, signal, interval, currentBar.Timestamp);

            if (!isViable) return null;

            decimal currentEquity = manager.GetTotalEquity();
            decimal maxTradeCap = currentEquity * 0.15m;

            // 2. GET STRATEGY ALLOCATION
            decimal allocationAmount = strategy.GetAllocationAmount(
                in currentBar, null, indicators, maxTradeCap, currentEquity,
                sharpeKelly, rawMacroScore, dynamicIdm, activePortfolioSize);

            if (allocationAmount <= 0) return null;

            // =====================================================================
            // 3. THE ATOMIC MARGIN GOVERNOR (The Squeeze)
            // =====================================================================
            decimal maxAllowableExposure = currentEquity * 1.50m;

            // Calculate space based ONLY on the live buffer we passed in
            decimal availableSpace = Math.Max(0, maxAllowableExposure - currentLiveExposure);

            // If the room is already full, don't even bother with the math below
            if (availableSpace <= 0)
            {
                _logger.LogWarning("Margin Cap strictly enforced for {Symbol}. Space: 0", symbol);
                return null;
            }

            // Shrink the trade if it exceeds the remaining budget
            decimal finalSafeAllocation = Math.Min(allocationAmount, availableSpace);

            int quantity = (int)Math.Floor(finalSafeAllocation / rawEntryPrice);
            if (quantity <= 0) return null;

            // =====================================================================

            var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;

            // 4. TRANSACTION COST CALCULATION
            decimal effectiveEntryPrice = _transactionCostService.CalculateEntryCost(rawEntryPrice, signal, symbol, interval, currentBar.Timestamp);
            decimal totalEntryCost = _transactionCostService.GetSpreadCost(rawEntryPrice, quantity, symbol, interval, currentBar.Timestamp);
            decimal totalCommissionCost = _transactionCostService.GetSpreadCost(rawEntryPrice, quantity, symbol, interval, currentBar.Timestamp);
            decimal totalSlippageCost = Math.Abs(effectiveEntryPrice - rawEntryPrice) * quantity;

            decimal totalCashOutlay = (quantity * rawEntryPrice) + totalEntryCost;

            // 5. FINAL ACCOUNT CHECK & EXECUTION
            if (await manager.CanOpenPosition(totalCashOutlay))
            {
                await manager.OpenPosition(symbol, interval, direction, quantity, effectiveEntryPrice, currentBar.Timestamp, totalCommissionCost);

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
                    CommissionCost = totalCommissionCost,
                    SlippageCost = totalSlippageCost,
                    TotalTransactionCost = totalCommissionCost + totalSlippageCost,
                    DynamicIdm = dynamicIdm,
                    MacroMultiplier = rawMacroScore,
                    ConvictionMultiplier = indicators.ContainsKey("RSI") ? indicators["RSI"] : 50m,
                    EntryReason = strategy.GetEntryReason(in currentBar, null, indicators),
                    EntryIndicatorsJson = JsonSerializer.Serialize(indicators)
                };
            }

            return null;
        }
        private async Task<TradeSummary?> ClosePositionAsync(
            IPortfolioManager manager,
            Position position,
            PriceModel currentBar,
            Guid runId,
            string strategyName,
            string exitReason,
            string interval,
            Dictionary<string, decimal> indicators,
            int? overrideQuantity = null)
        {
            // Determine if this is a full liquidation or a partial slice
            int exitQty = overrideQuantity ?? position.Quantity;
            if (exitQty <= 0) return null;

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
            decimal proRataEntryCost = position.TotalEntryCost * ((decimal)exitQty / position.Quantity);
            decimal totalTradeCosts = position.TotalEntryCost + totalExitCost;
            decimal netPnL = grossPnL - totalTradeCosts;

            decimal mfe = position.Direction == PositionDirection.Long
                ? (position.HighestPriceSinceEntry - position.AverageEntryPrice) * position.Quantity
                : (position.AverageEntryPrice - position.LowestPriceSinceEntry) * position.Quantity;

            // MAE will usually be a negative number representing how far in the red the trade went
            decimal mae = position.Direction == PositionDirection.Long
                ? (position.LowestPriceSinceEntry - position.AverageEntryPrice) * position.Quantity
                : (position.AverageEntryPrice - position.HighestPriceSinceEntry) * position.Quantity;

            // 5. Update Portfolio RAM State
            await manager.ClosePosition(strategyName, position.Symbol, position.Interval, position.Direction,
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
                ExitIndicatorsJson = JsonSerializer.Serialize(indicators),
                HoldingPeriodMinutes = (int)(currentBar.Timestamp - position.InitialEntryDate).TotalMinutes,
                MaxFavorableExcursion = mfe,
                MaxAdverseExcursion = mae,
            };
        }


        private PriceModel? FindBarAtTimestamp(ReadOnlySpan<PriceModel> span, DateTime target)
        {
            int low = 0;
            int high = span.Length - 1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int compare = span[mid].Timestamp.CompareTo(target);

                if (compare == 0) return span[mid];
                if (compare < 0) low = mid + 1;
                else high = mid - 1;
            }
            return null;
        }

        private ReadOnlyMemory<PriceModel> GetTimeSlice(ReadOnlyMemory<PriceModel> data, DateTime start, DateTime end)
        {
            var span = data.Span;
            int startIdx = -1;
            int endIdx = -1;

            // Binary Search for Start Index
            int left = 0;
            int right = span.Length - 1;
            while (left <= right)
            {
                int mid = left + ((right - left) / 2);
                if (span[mid].Timestamp >= start) { startIdx = mid; right = mid - 1; }
                else { left = mid + 1; }
            }

            if (startIdx == -1) return ReadOnlyMemory<PriceModel>.Empty;

            // Binary Search for End Index
            left = startIdx;
            right = span.Length - 1;
            while (left <= right)
            {
                int mid = left + ((right - left) / 2);
                if (span[mid].Timestamp <= end) { endIdx = mid; left = mid + 1; }
                else { right = mid - 1; }
            }

            if (endIdx == -1) endIdx = span.Length - 1;

            return data.Slice(startIdx, endIdx - startIdx + 1); // +1 to include the end index
        }

        private Dictionary<string, object> GetActiveParametersForDate(
            Dictionary<string, List<StrategyOptimizedParameterModel>> allCpos,
            string symbol,
            DateTime date)
        {
            if (allCpos == null || !allCpos.TryGetValue(symbol, out var cpos))
                return null;

            for (int i = cpos.Count - 1; i >= 0; i--)
            {
                if (cpos[i].EndDate <= date)
                {
                    if (string.IsNullOrWhiteSpace(cpos[i].OptimizedParametersJson))
                        return null;

                    return cpos[i].ParsedParmeters;
                }
            }

            return null;
        }

        private (StrategyOptimizedParameterModel? Cpo, decimal SharpeRatio) GetActiveCpoForDate(
            Dictionary<string, List<StrategyOptimizedParameterModel>> allCpos,
            string symbol,
            DateTime date)
        {
            if (allCpos == null || !allCpos.TryGetValue(symbol, out var cpos))
                return (null, 0m);

            for (int i = cpos.Count - 1; i >= 0; i--)
            {
                if (cpos[i].EndDate <= date)
                {
                    return (cpos[i], cpos[i].ParsedSharpeRatio);
                }
            }

            return (null, 0m);
        }

        private decimal? ExtractSharpe(string metricsJson)
        {
            if (string.IsNullOrWhiteSpace(metricsJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(metricsJson);

                if (doc.RootElement.TryGetProperty("SharpeRatio", out var srProp))
                {
                    return srProp.GetDecimal();
                }
            }
            catch (JsonException)
            {
                _logger.LogWarning("Invalid JSON format in CPO Metrics. Falling back to default Sharpe.");
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning("SharpeRatio property was not a valid decimal. Falling back to default Sharpe.");
            }

            return null;
        }

        private string GetSectorForSymbol(string symbol)
        {
            return _symbolToSectorMap.TryGetValue(symbol, out var sector) ? sector : "General";
        }

        private decimal CalculateRecentVolatility(ReadOnlyMemory<PriceModel> prices)
        {
            var span = prices.Span;
            if (span.Length < 10) return 0m; // Not enough data to be statistically valid

            var returns = new List<double>();
            for (int i = 1; i < span.Length; i++)
            {
                if (span[i - 1].ClosePrice > 0 && span[i].ClosePrice > 0)
                {
                    returns.Add(Math.Log((double)(span[i].ClosePrice / span[i - 1].ClosePrice)));
                }
            }

            if (returns.Count == 0) return 0m;

            double avgReturn = returns.Average();
            double variance = returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Count;

            // Annualize the daily variance (assume 252 trading days)
            return (decimal)(Math.Sqrt(variance) * Math.Sqrt(252));
        }
    }
}