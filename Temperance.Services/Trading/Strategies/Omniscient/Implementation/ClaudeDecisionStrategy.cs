//// In Constellations - ClaudeDecisionStrategy (Conceptual refinement)
//using Microsoft.Extensions.Logging;
//using TradingApp.src.Core.Models.MeanReversion;
//using Temperance.Data.Models.HistoricalPriceData;
//using Temperance.Data.Models.Trading;
//using Temperance.Services.Trading.Strategies;

//public class ClaudeDecisionStrategy : ITradingStrategy // Keep interface for consistency
//{
//    public string Name => "Claude_PaperTrading_Companion_V1";
//    private readonly IAthenaService _athenaService;
//    private readonly ILogger<ClaudeDecisionStrategy> _logger;
//    private string _basePromptTemplate;
//    private List<string> _targetSymbols;
//    private decimal _maxAllocationPerTradePct; // e.g., 0.10 for 10%

//    public ClaudeDecisionStrategy(IAthenaService athenaService, ILogger<ClaudeDecisionStrategy> logger)
//    {
//        _athenaService = athenaService;
//        _logger = logger;
//    }

//    public Dictionary<string, object> GetDefaultParameters() => new()
//    {
//        { "BasePromptFile", "Prompts/claude_papertrading_prompt.txt" },
//        { "TargetSymbols", new List<string> { "AAPL", "MSFT", "GOOGL", "TSLA", "NVDA" } }, // Example
//        { "MaxAllocationPerTradePct", 0.10m } // 10% of portfolio value
//    };

//    public void Initialize(decimal initialCapital, Dictionary<string, object> parameters)
//    {
//        // initialCapital might be less relevant if we fetch live paper account balance
//        string promptFilePath = (string)parameters.GetValueOrDefault("BasePromptFile", "default_prompt.txt")!;
//        try
//        {
//            _basePromptTemplate = File.ReadAllText(promptFilePath); // Ensure this file exists
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Failed to load base prompt file: {FilePath}", promptFilePath);
//            _basePromptTemplate = "Error: Could not load prompt."; // Fallback
//        }
//        _targetSymbols = (List<string>)parameters.GetValueOrDefault("TargetSymbols", new List<string>())!;
//        _maxAllocationPerTradePct = (decimal)parameters.GetValueOrDefault("MaxAllocationPerTradePct", 0.10m)!;

//        _logger.LogInformation("{StrategyName} initialized. Target Symbols: {Symbols}. Max Allocation/Trade: {Allocation}%",
//            Name, string.Join(",", _targetSymbols), _maxAllocationPerTradePct * 100);
//    }

//    // This method becomes the primary interface for the AiTradingOrchestrator
//    public async Task<ClaudeResponse?> RequestTradingDecisionsAsync(
//        PortfolioSnapshot portfolio,
//        Dictionary<string, IReadOnlyList<HistoricalPriceModel>> marketData, // Keyed by symbol
//        string generalNewsSummary)
//    {
//        string populatedPrompt = _basePromptTemplate
//            .Replace("{current_date}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
//            .Replace("{portfolio_cash}", portfolio.Cash.ToString("C"))
//            .Replace("{portfolio_equity}", portfolio.TotalValue.ToString("C"))
//            .Replace("{current_positions_json}", System.Text.Json.JsonSerializer.Serialize(portfolio.Positions))
//            .Replace("{target_symbols_list}", string.Join(", ", _targetSymbols))
//            .Replace("{market_data_summary_json}", System.Text.Json.JsonSerializer.Serialize(
//                marketData.ToDictionary(
//                    kvp => kvp.Key,
//                    kvp => kvp.Value.Any() ? new { Close = kvp.Value.Last().ClosePrice, Volume = kvp.Value.Last().Volume, Timestamp = kvp.Value.Last().Timestamp } : null
//                )
//            ))
//            .Replace("{news_summary}", generalNewsSummary)
//            .Replace("{max_allocation_pct_per_trade}", (_maxAllocationPerTradePct * 100).ToString("F0"));

//        _logger.LogInformation("Populated prompt for Claude: {Prompt}", populatedPrompt); // Log less in prod
//        return await _athenaService.GetClaudeTradingDecisionAsync(populatedPrompt);
//    }


//    // --- Other ITradingStrategy methods can be minimal for this forward-test only strategy ---
//    public int GetRequiredLookbackPeriod() => 1; // Not really used by orchestrator
//    public SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow) => SignalDecision.Hold; // Not called by orchestrator
//    public bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow) => false; // Logic handled by Claude's overall decision
//    public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal) => activeTrade; // Logic handled by Agora based on Claude's SELL
//    public decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxPortfolioRiskAllocation) => 0; // Claude suggests quantity
//}