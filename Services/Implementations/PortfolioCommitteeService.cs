using Microsoft.Identity.Client;
using System.Text.Json;
using Temperance.Constellations.Models.Backtest;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Ephemeris.Models.Constellations;
using Temperance.Ephemeris.Repositories.Constellations.Interfaces;
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

        // 1. Reality: Get last cycle's shadow performance
        var shadowReports = await _shadowPerformanceRepository.Get(sessionId);

        // 2. Prediction: Get the parameters LUDUS generated that are valid for this cycle start date
        var rawPredictions = await _strategyOptimizedParameterRepository.GetParametersValidOnDateAsync(strategyName, interval, cycleStartDate);

        // THE FIX: Group by symbol, and only let the absolute best one into the debate.
        var ludusPredictions = rawPredictions
            .Where(p => allowedUniverse.Contains(p.Symbol))
            .GroupBy(p => p.Symbol)
            .Select(g => g.First())
            .ToList();

        var candidates = new List<CandidateSleeve>();

        // 3. The Debate
        foreach (var ludusCpo in ludusPredictions)
        {
            var shadow = shadowReports.FirstOrDefault(s => s.Symbol == ludusCpo.Symbol);
            LudusMetricsModel metrics = new LudusMetricsModel();
            if (!string.IsNullOrWhiteSpace(ludusCpo.Metrics))
            {
                try
                {
                    metrics = JsonSerializer.Deserialize<LudusMetricsModel>(ludusCpo.Metrics) ?? new LudusMetricsModel();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse Metrics JSON for {Symbol}: {Msg}", ludusCpo.Symbol, ex.Message);
                }
            }
            var candidate = new CandidateSleeve
            {
                Symbol = ludusCpo.Symbol,
                OptimizationResultId = ludusCpo.Id,
                ExpectedSharpe = metrics.SharpeRatio,
                ShadowSharpe = shadow?.SharpeRatio ?? 0,
                ShadowWinRate = shadow?.WinRate ?? 0,
                //ShadowMaxDrawdown = shadow?.ShadowMaxDrawdown ?? 0,
                OptimizedParametersJson = ludusCpo.OptimizedParametersJson
            };

            // 4. The Scoring Algorithm
            //if (candidate.ShadowWinRate < 0.35m || candidate.ShadowMaxDrawdown < -0.15m)
            //    candidate.CompositeScore = -999;
            //else if (shadow == null || shadow.TotalTrades < 5)
            //    candidate.CompositeScore = candidate.ExpectedSharpe * 0.5m;
            //else
            //    candidate.CompositeScore = (candidate.ShadowSharpe * 0.7m) + (candidate.ExpectedSharpe * 0.3m);

            if (shadow == null || shadow.TotalTrades < 3)
            {
                // RELIANCE ON LUDUS EXPECTATION
                // If we have no shadow data, trust the optimizer's expected win rate, not Sharpe.
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

        // 5. The Draft Pick
        var draftedSymbols = candidates
            .Where(c => c.CompositeScore > -9000m) // ENFORCE THE VETO
            .OrderByDescending(c => c.CompositeScore)
            .Select(c => c.Symbol)
            .ToHashSet();

        // 6. Create the NEW Sleeves to INSERT
        var newSleevesToInsert = new List<WalkForwardSleeveModel>();

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
                InSampleMaxDrawdown = 0,
                OptimizedParametersJson = candidate.OptimizedParametersJson,
                IsActive = isDrafted,
                CreatedAt = DateTime.UtcNow,
                StrategyName = strategyName
            });
        }

        // 7. THE DAPPER BULK INSERT
        await _walkForwardSleeveRepository.InsertSleevesBulkAsync(newSleevesToInsert);

        _logger.LogInformation("Committee adjourned. {Count} active sleeves mandated and inserted.", draftedSymbols.Count);

        return candidates;
    }
}