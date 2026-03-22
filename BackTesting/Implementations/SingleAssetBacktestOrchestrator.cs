//using System.Collections.Concurrent;
//using System.Text.Json;
//using Temperance.Constellations.Models;
//using Temperance.Constellations.Models.MarketHealth;
//using Temperance.Constellations.Models.Trading;
//using Temperance.Constellations.Repositories.Interfaces;
//using Temperance.Ephemeris.Models.Constellations;
//using Temperance.Ephemeris.Models.Prices;
//using Temperance.Constellations.BackTesting.Interfaces;
//using Temperance.Services.Factories.Interfaces;
//using Temperance.Constellations.Services.Interfaces;
//using Temperance.Services.Trading.Strategies;

//namespace Temperance.Services.BackTesting.Implementations
//{
//    public class SingleSecurityBacktester : ISingleSecurityBacktester
//    {
//        private readonly ILogger<SingleSecurityBacktester> _logger;
//        private readonly IWalkForwardRepository _walkForwardRepository;
//        private readonly IStrategyFactory _strategyFactory;
//        private readonly ITransactionCostService _transactionCostService;
//        private readonly IPerformanceCalculator _performanceCalculator;
//        private readonly IGpuIndicatorService _gpuIndicatorService;
//        private readonly IMarketHealthService _marketHealthService;
//        private readonly ILiquidityService _liquidityService;

//        public SingleSecurityBacktester(
//            ILogger<SingleSecurityBacktester> logger,
//            IWalkForwardRepository walkForwardRepository,
//            IStrategyFactory strategyFactory,
//            ITransactionCostService transactionCostService,
//            IPerformanceCalculator performanceCalculator,
//            IGpuIndicatorService gpuIndicatorService,
//            IMarketHealthService marketHealthService,
//            ILiquidityService liquidityService)
//        {
//            _logger = logger;
//            _walkForwardRepository = walkForwardRepository;
//            _strategyFactory = strategyFactory;
//            _transactionCostService = transactionCostService;
//            _performanceCalculator = performanceCalculator;
//            _gpuIndicatorService = gpuIndicatorService;
//            _marketHealthService = marketHealthService;
//            _liquidityService = liquidityService;
//        }

//        public async Task<PerformanceSummary> RunAsync(WalkForwardSessionModel session, string symbol, DateTime startDate, DateTime endDate)
//        {
//            StrategyOptimizedParameters parameters = await _walkForwardRepository.GetOptimizedParametersForSymbol(session.SessionId, symbol, startDate.AddDays(-1));
//            if (parameters == null)
//            {
//                _logger.LogWarning("No optimized parameters for {Symbol} in session {SessionId}. Skipping.", symbol, session.SessionId);
//                return null;
//            }
//            else
//            {
//                _logger.LogInformation("Running backtest for {Symbol} from {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd} with optimized parameters ID {ParametersId}.",
//                    symbol, startDate, endDate, parameters.Id);
//            }

//                var strategyParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters.OptimizedParametersJson);
//            var strategy = _strategyFactory.CreateStrategy<ISingleAssetStrategy>(session.StrategyName, session.InitialCapital, strategyParameters);

//            int lookback = strategy.GetRequiredLookbackPeriod();
//            var dataStartDate = startDate.AddDays(-lookback * 2);

//            var priceData = (await _historicalPriceService.GetHistoricalPrices(symbol, "60min", dataStartDate, endDate)).ToList();
//            if (priceData.Count < lookback)
//            {
//                _logger.LogWarning("Not enough historical data for {Symbol} ({Count} bars). Skipping.", symbol, priceData.Count);
//                return null;
//            }

//            var indicators = PreCalculateIndicators(priceData, strategy);
//            var timestampIndexMap = priceData.Select((data, index) => new { data.Timestamp, index }).ToDictionary(x => x.Timestamp, x => x.index);

//            var marketHealthCache = new ConcurrentDictionary<DateTime, MarketHealthScore>();
//            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
//                marketHealthCache.TryAdd(day, await _marketHealthService.GetCurrentMarketHealth(day));

//            var trades = new List<TradeSummary>();
//            Position openPosition = null;
//            decimal currentCapital = session.InitialCapital;

//            foreach (var currentBar in priceData.Where(p => p.Timestamp >= startDate && p.Timestamp <= endDate))
//            {
//                if (!timestampIndexMap.TryGetValue(currentBar.Timestamp, out var globalIndex) || globalIndex < lookback) continue;

//                var pointInTimeData = priceData.Take(globalIndex + 1).ToList();
//                var currentIndicatorValues = GetIndicatorsForBar(indicators, globalIndex);

//                if (openPosition != null)
//                {
//                    if (strategy.ShouldExitPosition(openPosition, currentBar, pointInTimeData, currentIndicatorValues))
//                    {
//                        var exitReason = strategy.GetExitReason(openPosition, currentBar, pointInTimeData, currentIndicatorValues);
//                        var closedTrade = await FinalizeTradeAsync(openPosition, currentBar.ClosePrice, currentBar.Timestamp, exitReason);
//                        trades.Add(closedTrade);
//                        currentCapital += closedTrade.ProfitLoss.Value;
//                        openPosition = null;
//                    }
//                }

//                if (openPosition == null)
//                {
//                    marketHealthCache.TryGetValue(currentBar.Timestamp.Date, out var marketHealth);
//                    var signal = strategy.GenerateSignal(currentBar, null, pointInTimeData, currentIndicatorValues, marketHealth);

//                    if (signal != SignalDecision.Hold)
//                    {
//                        long minimumAdv = strategy.GetMinimumAverageDailyVolume();
//                        if (!_liquidityService.IsSymbolLiquidAtTime(symbol, "60min", minimumAdv, currentBar.Timestamp, 20, priceData))
//                            continue;

//                        decimal allocationAmount = strategy.GetAllocationAmount(
//                            currentBar, pointInTimeData, currentIndicatorValues,
//                            (decimal)session.InitialCapital * 0.02m,
//                            (decimal)currentCapital, // currentTotalEquity
//                            0.5m, // kellyHalfFraction (using a default for stateless backtest)
//                            0, // currentPyramidEntries (pyramiding not handled in this stateless model)
//                            marketHealth
//                        );

//                        if (allocationAmount <= 0) continue;

//                        int quantity = (int)Math.Floor(allocationAmount / currentBar.ClosePrice);
//                        if (quantity <= 0) continue;

//                        var direction = (signal == SignalDecision.Buy) ? PositionDirection.Long : PositionDirection.Short;
//                        var entryCosts = _transactionCostService.CalculateEntryCost(currentBar.ClosePrice, signal);
//                        decimal totalCashOutlay = (quantity * currentBar.ClosePrice) + entryCosts;

//                        if (totalCashOutlay > currentCapital) continue;

//                        openPosition = new Position
//                        {
//                            Symbol = symbol,
//                            EntryDate = currentBar.Timestamp,
//                            EntryPrice = currentBar.ClosePrice,
//                            Quantity = quantity,
//                            Direction = direction,
//                        };
//                    }
//                }
//            }

//            if (openPosition != null)
//            {
//                var lastBar = priceData.Last();
//                trades.Add(await FinalizeTradeAsync(openPosition, lastBar.ClosePrice, lastBar.Timestamp, "End of Period"));
//            }

//            BacktestResult backtest = new BacktestResult() { Trades = trades };

//            var metrics = _performanceCalculator.CalculatePerformanceMetrics(backtest, session.InitialCapital);
//            return new PerformanceSummary
//            {
//                Symbol = symbol,
//                SharpeRatio = (decimal)backtest.SharpeRatio,
//                ProfitLoss = (decimal)backtest?.TotalProfitLoss,
//                TotalTrades = backtest.TotalTrades,
//                WinRate = (decimal)backtest?.WinRate,
//                TotalTransactionCost = (decimal)backtest?.Trades?.Sum(t => t.TotalTransactionCost)
//            };
//        }

//        private Dictionary<string, decimal[]> PreCalculateIndicators(IReadOnlyList<PriceModel> priceData, ISingleAssetStrategy strategy)
//        {
//            var closePrices = priceData.Select(p => (decimal)p.ClosePrice).ToArray();
//            int lookback = strategy.GetRequiredLookbackPeriod();

//            var sma = _gpuIndicatorService.CalculateSma(closePrices, lookback);
//            var stdDev = _gpuIndicatorService.CalculateStdDev(closePrices, lookback);
//            var upperBand = sma.Zip(stdDev, (m, s) => m + (strategy.GetStdDevMultiplier() * s)).ToArray();
//            var lowerBand = sma.Zip(stdDev, (m, s) => m - (strategy.GetStdDevMultiplier() * s)).ToArray();
//            var rsi = strategy.CalculateRSI(closePrices, 14);

//            return new Dictionary<string, decimal[]>
//            {
//                { "SMA", sma }, { "UpperBand", upperBand }, { "LowerBand", lowerBand }, { "RSI", rsi }
//            };
//        }

//        private Dictionary<string, decimal> GetIndicatorsForBar(Dictionary<string, decimal[]> allIndicators, int globalIndex)
//        {
//            var values = new Dictionary<string, decimal>();
//            foreach (var indicator in allIndicators)
//            {
//                values[indicator.Key] = globalIndex < indicator.Value.Length ? indicator.Value[globalIndex] : decimal.MinValue;
//            }
//            return values;
//        }

//        private async Task<TradeSummary> FinalizeTradeAsync(Position position, decimal exitPrice, DateTime exitDate, string exitReason)
//        {
//            decimal exitCosts = _transactionCostService.CalculateExitCost(exitPrice, position.Direction);
//            decimal grossPnl = (exitPrice - position.EntryPrice) * position.Quantity * (position.Direction == PositionDirection.Long ? 1 : -1);
//            decimal totalTxCost = position.TotalEntryCost + exitCosts;

//            return new TradeSummary
//            {
//                Symbol = position.Symbol,
//                EntryDate = position.EntryDate,
//                ExitDate = exitDate,
//                EntryPrice = position.EntryPrice,
//                ExitPrice = exitPrice,
//                Quantity = position.Quantity,
//                Direction = position.Direction.ToString(),
//                ProfitLoss = grossPnl - totalTxCost,
//                GrossProfitLoss = grossPnl,
//                TotalTransactionCost = totalTxCost,
//                ExitReason = exitReason
//            };
//        }
//    }
//}