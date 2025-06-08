using Microsoft.Extensions.Logging;
using Temperance.Data.Models.Conductor;         // For DTOs including AiTradingDecisionResponseDto, PortfolioSnapshotDto, SimpleBarDto
using Temperance.Data.Models.HistoricalPriceData; // For HistoricalPriceModel (used in ITradingStrategy interface)
using Temperance.Data.Models.Trading;             // For Position, SignalDecision, TradeSummary (used in ITradingStrategy interface)
using Temperance.Services.Services.Interfaces;
using Temperance.Services.Trading.Strategies;

namespace Temperence.Services.Trading.Strategies.Omniscient.Implementation
{
    public class ClaudeDecisionStrategy : ITradingStrategy
    {
        public string Name => "Claude_PaperTrading_Companion_V1";
        private readonly IConductorService _conductorService;
        private readonly ILogger<ClaudeDecisionStrategy> _logger;
        private string _userPromptTemplate = string.Empty;
        private string _systemPromptForClaude = string.Empty;
        private List<string> _targetSymbols = new List<string>();
        private decimal _maxAllocationPerTradePct;

        public ClaudeDecisionStrategy(IConductorService conductorService, ILogger<ClaudeDecisionStrategy> logger)
        {
            _conductorService = conductorService;
            _logger = logger;
        }

        public Dictionary<string, object> GetDefaultParameters() => new()
        {
            { "UserPromptTemplateFile", "Prompts/claude_papertrading_user_prompt.txt" },
            { "SystemPrompt", @"You are a helpful, risk-averse paper trading assistant. Your goal is to make sensible paper trading decisions based on the provided information. You MUST respond in the requested JSON format as specified in the user prompt. Do not include any explanatory text outside of the JSON structure itself." },
            { "TargetSymbols", new List<string> { "AAPL", "MSFT", "GOOGL", "TSLA", "NVDA" } }, // Example default symbols
            { "MaxAllocationPerTradePct", 0.05m } // Default 5% of portfolio equity per trade
        };

        public void Initialize(decimal initialCapital, Dictionary<string, object> parameters)
        {
            string userPromptFilePath = (string)parameters.GetValueOrDefault("UserPromptTemplateFile", "default_user_prompt.txt")!;
            try
            {
                string fullPath = Path.Combine(AppContext.BaseDirectory, userPromptFilePath);
                if (File.Exists(fullPath))
                {
                    _userPromptTemplate = File.ReadAllText(fullPath);
                    _logger.LogInformation("Successfully loaded user prompt template from: {FilePath}", fullPath);
                }
                else
                {
                    _logger.LogError("User prompt template file not found: {FilePath}. Using fallback.", fullPath);
                    _userPromptTemplate = GetFallbackUserPromptTemplate();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user prompt template file: {FilePath}. Using fallback.", userPromptFilePath);
                _userPromptTemplate = GetFallbackUserPromptTemplate();
            }

            _systemPromptForClaude = (string)parameters.GetValueOrDefault("SystemPrompt", "You are a trading AI that responds in JSON.")!;
            _targetSymbols = (List<string>)parameters.GetValueOrDefault("TargetSymbols", new List<string> { "ERROR_NO_SYMBOLS" })!;
            _maxAllocationPerTradePct = (decimal)parameters.GetValueOrDefault("MaxAllocationPerTradePct", 0.05m)!;

            _logger.LogInformation("Strategy '{StrategyName}' initialized. Target Symbols: {Symbols}. Max Allocation/Trade: {Allocation}%. System Prompt Loaded. User Prompt Template Loaded (check logs for status).",
                Name, string.Join(",", _targetSymbols), _maxAllocationPerTradePct * 100);
        }

        public async Task<AiTradingDecisionResponseDto?> RequestTradingDecisionsAsync(
            PortfolioSnapshotDto portfolio,
            Dictionary<string, List<SimpleBarDto>> marketData, // marketData from orchestrator
            string generalNewsSummary)
        {
            if (string.IsNullOrWhiteSpace(_userPromptTemplate) || _userPromptTemplate.Contains("CRITICAL FALLBACK PROMPT"))
            {
                _logger.LogError("User prompt template is not loaded correctly or is fallback. Cannot request trading decisions effectively.");
                return new AiTradingDecisionResponseDto { Error = "User prompt template not properly loaded." };
            }

            // ---- MODIFICATION START: Manual dictionary creation ----
            var marketDataForJson = new Dictionary<string, object>();
            foreach (var kvp in marketData) // marketData is Dictionary<string, List<SimpleBarDto>>
            {
                if (kvp.Value != null && kvp.Value.Any())
                {
                    // Select only Timestamp and ClosePrice for the JSON summary
                    marketDataForJson[kvp.Key] = kvp.Value.Select(bar => new { bar.Timestamp, bar.ClosePrice }).ToList();
                }
                else
                {
                    // Use an empty list if no bars for a symbol, to maintain consistent structure
                    marketDataForJson[kvp.Key] = new List<object>();
                }
            }
            string marketDataSummaryJsonString = System.Text.Json.JsonSerializer.Serialize(marketDataForJson);
            // ---- MODIFICATION END ----

            // Populate the user prompt template
            string populatedUserPrompt = _userPromptTemplate
                .Replace("{current_date}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
                .Replace("{portfolio_cash}", portfolio.Cash.ToString("C"))
                .Replace("{portfolio_equity}", portfolio.TotalValue.ToString("C"))
                .Replace("{current_positions_json}", System.Text.Json.JsonSerializer.Serialize(
                    portfolio.Positions?.Select(p => new { p.Symbol, p.Quantity, p.AverageEntryPrice }) ?? Enumerable.Empty<object>()
                 ))
                .Replace("{target_symbols_list}", string.Join(", ", _targetSymbols))
                .Replace("{market_data_summary_json}", marketDataSummaryJsonString) // Use the manually created JSON string
                .Replace("{news_summary}", generalNewsSummary)
                .Replace("{max_allocation_pct_per_trade}", (_maxAllocationPerTradePct * 100).ToString("F0"));

            _logger.LogInformation("Requesting AI trading decisions from Conductor. Target Symbols: {TargetSymbols}", string.Join(", ", _targetSymbols));
            _logger.LogDebug("System Prompt for Conductor (to forward to Athena): {SystemPrompt}", _systemPromptForClaude);
            _logger.LogDebug("User Prompt for Conductor (to forward to Athena): {UserPrompt}", populatedUserPrompt);

            var promptDtoForConductor = new AiTradingDecisionPromptDto
            {
                UserPrompt = populatedUserPrompt,
                SystemPrompt = _systemPromptForClaude
            };

            return await _conductorService.GetAiTradingDecisionAsync(promptDtoForConductor);
        }

        // --- Implementation of ITradingStrategy methods (minimal, as this strategy is orchestrated differently) ---
        public int GetRequiredLookbackPeriod()
        {
            _logger.LogDebug("{StrategyName}.GetRequiredLookbackPeriod() called.", Name);
            // This strategy's "lookback" is embodied in the market data summary passed to RequestTradingDecisionsAsync.
            // Return a nominal value if your framework requires it.
            return 5; // e.g., if we typically feed 5 bars of summary data
        }

        public SignalDecision GenerateSignal(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow)
        {
            // This method is unlikely to be called by the AiTradingOrchestrator for this strategy.
            // Decisions are made periodically by RequestTradingDecisionsAsync.
            _logger.LogWarning("{StrategyName}.GenerateSignal() called - this strategy makes decisions periodically, not per bar.", Name);
            return SignalDecision.Hold;
        }

        public bool ShouldExitPosition(Position position, HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow)
        {
            // Exit logic is handled by Claude providing a "SELL" decision in RequestTradingDecisionsAsync.
            _logger.LogWarning("{StrategyName}.ShouldExitPosition() called - exit logic is part of periodic AI decision.", Name);
            return false;
        }

        public TradeSummary ClosePosition(TradeSummary activeTrade, HistoricalPriceModel currentBar, SignalDecision exitSignal)
        {
            // Position closing is handled by AiTradingOrchestrator calling ExecuteTradeAsync via Conductor
            // based on Claude's "SELL" signal. This method might not be directly used by the orchestrator
            // unless the framework strictly requires it for bookkeeping.
            _logger.LogWarning("{StrategyName}.ClosePosition() called - actual closing is via Conductor/Agora.", Name);
            if (activeTrade != null)
            {
                activeTrade.ExitDate = currentBar.Timestamp; // Example if called
                activeTrade.ExitPrice = currentBar.ClosePrice;
            }
            return activeTrade!; // Ensure non-null if interface expects it
        }

        public decimal GetAllocationAmountAi(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocation)
        {
            // Quantity/Allocation is determined by Claude's output and validated by the orchestrator.
            _logger.LogWarning("{StrategyName}.GetAllocationAmount() called - allocation derived from Claude's suggested quantity.", Name);
            return 0;
        }

        private string GetFallbackUserPromptTemplate()
        {
            // This should match the structure expected by the placeholders in RequestTradingDecisionsAsync
            return @"CRITICAL FALLBACK PROMPT: User prompt template file was not found.
                Based on the current portfolio, recent market data, and news, provide trading decisions.
                Current Date: {current_date}
                Portfolio Cash: {portfolio_cash}
                Portfolio Equity: {portfolio_equity}
                Current Positions (JSON format): {current_positions_json}
                Target Symbols for Analysis: {target_symbols_list}
                Market Data Summary (JSON format, recent closes/volumes): {market_data_summary_json}
                Recent Market News Summary: {news_summary}
                Considering a maximum allocation of {max_allocation_pct_per_trade}% of total portfolio equity per new trade,
                Provide a list of trading decisions (BUY, SELL, HOLD) for the target symbols.
                For BUY/SELL, specify symbol and quantity (as an integer number of shares).
                For SELL, only list symbols currently held in the portfolio.
                Your response MUST be in JSON format like this (do not add any text before or after the JSON block):
                {
                  ""decisions"": [ { ""symbol"": ""XYZ"", ""action"": ""BUY"", ""quantity"": 0, ""reasoning"": ""Fallback prompt used."" } ],
                  ""overall_reasoning_memo"": ""Fallback prompt activated due to missing template file.""
                }";
        }

        public decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocation)
        {
            throw new NotImplementedException();
        }

        public decimal GetAllocationAmount(HistoricalPriceModel currentBar, IReadOnlyList<HistoricalPriceModel> historicalDataWindow, decimal maxTradeAllocationInitialCapital, decimal currentTotalEquity, decimal kellyHalfFraction)
        {
            throw new NotImplementedException();
        }

        public long GetMinimumAverageDailyVolume()
        {
            throw new NotImplementedException();
        }
    }
}