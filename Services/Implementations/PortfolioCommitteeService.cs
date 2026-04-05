using Microsoft.Identity.Client;
using System.Text.Json;
using Temperance.Constellations.Models.Backtest;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Repositories.Constellations.Interfaces;
using Temperance.Ephemeris.Repositories.Ludus.Implementations;
using Temperance.Ephemeris.Repositories.Ludus.Interfaces;

public class PortfolioCommitteeService : IPortfolioCommitteeService
{
    private readonly IShadowPerformanceRepository _shadowPerformanceRepository;
    private readonly IWalkForwardSleeveRepository _walkForwardSleeveRepository;
    private readonly IStrategyOptimizedParametersRepository _strategyOptimizedParameterRepository;
    private readonly ILogger<PortfolioCommitteeService> _logger;

    public PortfolioCommitteeService(ILogger<PortfolioCommitteeService> logger,
        IShadowPerformanceRepository shadowPerformanceRepository,
        IWalkForwardSleeveRepository walkForwardSleeveRepository,
        IStrategyOptimizedParametersRepository strategyOptimizedParameterRepository)
    {
        _shadowPerformanceRepository = shadowPerformanceRepository;
        _walkForwardSleeveRepository = walkForwardSleeveRepository;
        _strategyOptimizedParameterRepository = strategyOptimizedParameterRepository;
        _logger = logger;
    }

    public async Task<List<CandidateSleeve>> HoldPromotionCommitteeAsync(
    Guid sessionId,
    string strategyName,
    string interval,
    DateTime cycleStartDate,
    int maxActivePositions,
    IReadOnlySet<string> allowedUniverse)
    {
        _logger.LogInformation("Holding Portfolio Committee for Session {SessionId} at {Date}", sessionId, cycleStartDate.ToShortDateString());

        // 1. CONCURRENT FETCH: Hit the database for both sets of data at the exact same time
        var shadowTask = _shadowPerformanceRepository.Get(sessionId);
        var predictionTask = _strategyOptimizedParameterRepository.GetParametersValidOnDateAsync(strategyName, interval, cycleStartDate);
        await Task.WhenAll(shadowTask, predictionTask);

        // 2. THE O(1) HASH MAP: Convert the shadow list to a dictionary for instant lookups
        // We use GroupBy/FirstOrDefault in case there are accidental duplicate records in the DB
        var shadowDict = shadowTask.Result
            .GroupBy(s => s.Symbol)
            .ToDictionary(g => g.Key, g => g.First());

        // 3. FAST DEDUPLICATION: Avoid GroupBy().First() which creates intermediate memory allocations
        var uniquePredictions = new Dictionary<string, StrategyOptimizedParameterModel>();
        foreach (var p in predictionTask.Result)
        {
            if (allowedUniverse.Contains(p.Symbol) && !uniquePredictions.ContainsKey(p.Symbol))
            {
                uniquePredictions[p.Symbol] = p;
            }
        }

        // Pre-allocate list memory since we know exactly how many items we have
        int capacity = uniquePredictions.Count;
        var candidates = new List<CandidateSleeve>(capacity);
        var newSleevesToInsert = new List<WalkForwardSleeveModel>(capacity);

        // 4. THE O(N) SCORING LOOP
        foreach (var kvp in uniquePredictions)
        {
            var symbol = kvp.Key;
            var ludusCpo = kvp.Value;

            // INSTANT LOOKUP - No O(N^2) scanning
            shadowDict.TryGetValue(symbol, out var shadow);

            LudusMetricsModel metrics = new LudusMetricsModel();
            if (!string.IsNullOrWhiteSpace(ludusCpo.Metrics))
            {
                try
                {
                    metrics = JsonSerializer.Deserialize<LudusMetricsModel>(ludusCpo.Metrics) ?? new LudusMetricsModel();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse Metrics JSON for {Symbol}: {Msg}", symbol, ex.Message);
                }
            }

            var candidate = new CandidateSleeve
            {
                Symbol = symbol,
                OptimizationResultId = ludusCpo.Id,
                ExpectedSharpe = metrics.SharpeRatio,
                ShadowSharpe = shadow?.SharpeRatio ?? 0,
                ShadowWinRate = shadow?.WinRate ?? 0,
                OptimizedParametersJson = ludusCpo.OptimizedParametersJson
            };

            // 5. The Scoring Algorithm
            if (shadow == null || shadow.TotalTrades < 3)
            {
                decimal expectedDensity = Math.Min(metrics.TotalTrades / 50m, 1.0m);
                candidate.CompositeScore = (metrics.WinRate * 0.70m) + (expectedDensity * 0.30m);
                if (metrics.WinRate < 0.52m) candidate.CompositeScore = -9999m;
            }
            else
            {
                if (candidate.ShadowWinRate < 0.52m)
                {
                    candidate.CompositeScore = -9999m;
                }
                else
                {
                    decimal normalizedDensity = Math.Min(shadow.TotalTrades / 50m, 1.0m);
                    candidate.CompositeScore = (candidate.ShadowWinRate * 0.70m) + (normalizedDensity * 0.30m);
                }
            }

            candidates.Add(candidate);
        }

        // 6. THE DRAFT PICK (Fixed Max Positions Limit)
        var draftedSymbols = candidates
            .Where(c => c.CompositeScore > -9000m)
            .OrderByDescending(c => c.CompositeScore)
            .Take(maxActivePositions) // <-- CRITICAL FIX: Only take the Top N!
            .Select(c => c.Symbol)
            .ToHashSet();

        // 7. SLEEVE GENERATION
        foreach (var candidate in candidates)
        {
            bool isDrafted = draftedSymbols.Contains(candidate.Symbol);
            candidate.IsPromoted = isDrafted;

            newSleevesToInsert.Add(new WalkForwardSleeveModel
            {
                SleeveId = Guid.NewGuid(),
                SessionId = sessionId,
                TradingPeriodStartDate = cycleStartDate,
                Symbol = candidate.Symbol,
                Interval = interval,
                OptimizationResultId = candidate.OptimizationResultId,
                InSampleMaxDrawdown = 0, // Optionally update this if it matters
                OptimizedParametersJson = candidate.OptimizedParametersJson,
                IsActive = isDrafted,
                CreatedAt = DateTime.UtcNow,
                StrategyName = strategyName
            });
        }

        // 8. THE DAPPER BULK INSERT
        await _walkForwardSleeveRepository.InsertSleevesBulkAsync(newSleevesToInsert);

        _logger.LogInformation("Committee adjourned. {Count} active sleeves mandated and inserted.", draftedSymbols.Count);

        return candidates;
    }
}