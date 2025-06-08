using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Temperance.Services.Services.Interfaces; 
using Temperance.Data.Models.Conductor;
using System.Text;
using System.Text.Json.Nodes;

namespace Temperance.Services.Services.Implementations
{
    public class AiTradingOrchestrator
    {
        private readonly ILogger<AiTradingOrchestrator> _logger;
        private readonly IConductorService _conductorService;

        public readonly List<string> _targetSymbols;
        private readonly string _claudeTradingSystemPrompt;
        private readonly string _claudeTradingUserPromptTemplate;

        public AiTradingOrchestrator(
            ILogger<AiTradingOrchestrator> logger,
            IConductorService conductorService)
        {
            _logger = logger;
            _conductorService = conductorService;

            _claudeTradingSystemPrompt = @"You are a helpful, risk-averse paper trading assistant.
                Your goal is to make sensible paper trading decisions based on the provided information.
                You MUST respond in the requested JSON format. Do not include any explanatory text outside of the JSON structure itself.
                When suggesting quantities, consider a paper portfolio of $100,000 and a max allocation of 2% per trade.
                You are going to be provided a list of {sectors} and will be making balanced investments across all available sectors, 
                As a seasoned veteren value investor, you will also get {current_positions_json} and use this data as you see fit. ";
            _claudeTradingUserPromptTemplate = @"Based on the current portfolio, recent market data, and news, provide trading decisions.
                Current Date: {current_date}
                Paper Portfolio Cash: ${portfolio_cash}
                Current Paper Positions:
                {current_positions_json}

                Recent Market News Summary:
                {news_summary}

                Market Data for {relevant_symbols_list} (last few closes):
                {market_data_summary_json}

                Provide a list of trading decisions (BUY, SELL, HOLD) for these symbols: {symbols_to_analyze_list}.
                For BUY/SELL, specify symbol and quantity. For SELL, only list symbols you currently hold.
                Your response MUST be in JSON format like this:
                {
                  ""decisions"": [
                    { ""symbol"": ""AAPL"", ""action"": ""BUY"", ""quantity"": 10, ""reasoning"": ""Positive earnings outlook and technical breakout."" },
                    { ""symbol"": ""MSFT"", ""action"": ""SELL"", ""quantity"": 5, ""reasoning"": ""Reached profit target, diversifying."" },
                    { ""symbol"": ""GOOGL"", ""action"": ""HOLD"", ""reasoning"": ""Monitoring for clearer signal."" }
                  ],
                  ""overall_reasoning_memo"": ""Market shows mixed signals. Focused on tech sector opportunities...""
                }";
        }

        public async Task LuminaraSectorStrategy(Guid runId)
        {
            List<string> targetSectors = await _conductorService.GetSectors();

            _logger.LogInformation("RunId: {RunId} - Starting AI Trading Cycle at {Timestamp}", runId, DateTime.UtcNow);

            var invokeFundamentals = await _conductorService.GetFundamentalsFromSector(
                runId, null, sectors: string.Join(",", targetSectors));

            try
            {
                PortfolioSnapshotDto? portfolio = await _conductorService.GetPortfolioSnapshotAsync();
                if (portfolio == null)
                {
                    _logger.LogError("RunId: {RunId} - Failed to get portfolio snapshot. Aborting cycle.", runId);
                    return;
                }

                var symbolsPrompt = $@"Please provide a list of symbols, based of your analysis of the fundamental data: {invokeFundamentals}, 
                    and determine the best value stocks, following closely to the principles of Benjamin Grahan, Warren Buffet and Charlie Monger.
                    Please consider my current Portfolio SnapShot {portfolio}. Please do not provide more than 100 symbols";

                string aiSuggestedSymbols = await _conductorService.GetGenericResponse(symbolsPrompt);
                var aiSuggestedSymbolsList = JsonSerializer.Deserialize<List<string>>(aiSuggestedSymbols) ?? new List<string>();
                string currentPosition = JsonSerializer.Serialize(portfolio.Positions);
                _logger.LogInformation("RunId: {RunId} - Portfolio: Cash ${Cash}, TotalValue ${TotalValue}, Positions: {PosCount}",
                    runId, portfolio.Cash, portfolio.TotalValue, portfolio.Positions?.Count ?? 0);

                var marketDataSummaryForLlm = new JsonObject();
                var newsSummaryBuilder = new StringBuilder();
                var latestPrices = new Dictionary<string, decimal>();
                var allSymbols = await _conductorService.GetSecurities();

                foreach(var symbol in aiSuggestedSymbolsList)
                {
                    _logger.LogInformation("RunId: {RunId} - Fetching complex market insight for {Symbol}", runId, symbol);
                    string insightResponseJson = await _conductorService.GetComplexMarketInsights(
                        runId, symbol, "HistoricalPrices, LatestNews, Market Indicators", null, null, null, symbol, null);
                    

                    if (string.IsNullOrWhiteSpace(insightResponseJson))
                    {
                        _logger.LogWarning("RunId: {RunId} - Received null or empty insight response for {Symbol}", runId, symbol);
                        continue;
                    }
                }
            }
            catch(Exception ex)
            {
                return;
            }

            //var tradingDecisionPrompt = @$"{}I need you to take the {insightResponseJson} and you must make a trading decision in this format!
            //                No trade can exceed 1% of the portfolio value, which is ${portfolio.TotalValue}, please also be aware with the cash available: ${portfolio.Cash}
            //            {{
            //                  ""type"": ""tool_use_request"", // Or similar indicator
            //                  ""tool_name"": ""MakeTradingDecision"",
            //                  ""tool_input_arguments"": {{ // Arguments matching your defined schema
            //                    ""strategyId"": ""AI_Luminara_Orchestrator_AAPL_run123"",
            //                    ""symbol"": ""AAPL"",
            //                    ""currentPrice"": 184.40, // LLM extracts/deduces this
            //                    ""aiPrompts"": {{ // LLM populates this object
            //                      ""SignalType"": ""BUY"",
            //                      ""Quantity"": 27, // Example: (0.05 * 100000) / 184.40
            //                      ""TargetPrice"": 188.00,
            //                      ""ReasoningFromLLM"": ""Based on recent news and price action, a mean reversion opportunity is identified.""
            //                    }},
            //                    ""additionalStrategyData"": null
            //                  }}
            //                }}    
            //        ";
        }

        public async Task ExecuteTradingCycleAsyncOld(Guid runId)
        {
            _logger.LogInformation("RunId: {RunId} - Starting AI Trading Cycle (all calls via Conductor) at {Timestamp}", runId, DateTime.UtcNow);

            try
            {
                _logger.LogInformation("RunId: {RunId} - Fetching portfolio snapshot from Conductor.", runId);
                PortfolioSnapshotDto? portfolio = await _conductorService.GetPortfolioSnapshotAsync();
                if (portfolio == null)
                {
                    _logger.LogError("RunId: {RunId} - Failed to get portfolio snapshot from Conductor. Aborting cycle.", runId);
                    return;
                }
                // ACCESSING DTO PROPERTIES: Make sure these property names match your DTO definition EXACTLY (case matters).
                _logger.LogInformation("RunId: {RunId} - Portfolio: Cash ${Cash}, TotalValue ${TotalValue}, Positions: {PosCount}",
                    runId, portfolio.Cash, portfolio.TotalValue, portfolio.Positions?.Count ?? 0);

                // 2. Get Market Data (via Conductor)
                var marketDataForPrompt = new Dictionary<string, object>();
                foreach (var symbol in _targetSymbols)
                {
                    // VERIFY: Ensure Temperance.IConductorService has GetRecentBarsAsync(string, int, string)
                    // VERIFY: Ensure Temperance.Data.Models.Conductor.SimpleBarDto has Timestamp and ClosePrice
                    List<SimpleBarDto>? bars = await _conductorService.GetRecentBarsAsync(symbol, 5, "1d");
                    if (bars != null && bars.Any())
                    {
                        marketDataForPrompt[symbol] = bars.Select(b => new { b.Timestamp, b.ClosePrice }).ToList();
                    }
                }

                // 3. Get News Summary (via Conductor, which then calls Athena)
                _logger.LogInformation("RunId: {RunId} - Fetching news summary via Conductor.", runId);
                // VERIFY: Ensure Temperance.Data.Models.Conductor.AiNewsSummaryPromptDto is defined
                var newsPromptForConductor = new AiNewsSummaryPromptDto
                {
                    Context = $"Major market-moving financial news relevant to US equities (especially for symbols: {string.Join(", ", _targetSymbols)}) from the last 24 hours.",
                    DesiredSummaryLength = "2-3 sentences",
                    SystemPrompt = "You are a concise financial news summarizer."
                };
                // VERIFY: Ensure Temperance.IConductorService has GetAiNewsSummaryAsync(AiNewsSummaryPromptDto)
                string newsSummary = await _conductorService.GetAiNewsSummaryAsync(newsPromptForConductor) ?? "No news summary available via Conductor.";
                _logger.LogInformation("RunId: {RunId} - News Summary: {NewsSummary}", runId, newsSummary);

                // 4. Construct the Full User Prompt for Claude
                string currentDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
                string portfolioCash = portfolio.Cash.ToString("F2"); // Accessing Cash again
                // VERIFY: Ensure Temperance.Data.Models.Conductor.PortfolioPositionDto has Symbol, Quantity, AverageEntryPrice
                string currentPositionsJson = JsonSerializer.Serialize(
                    portfolio.Positions?.Select(p => new { p.Symbol, p.Quantity, p.AverageEntryPrice }) ?? Enumerable.Empty<object>()
                );
                string marketDataSummaryJson = JsonSerializer.Serialize(marketDataForPrompt);
                string symbolsToAnalyzeList = string.Join(", ", _targetSymbols);

                string finalUserPromptForClaude = _claudeTradingUserPromptTemplate
                    .Replace("{current_date}", currentDate)
                    .Replace("{portfolio_cash}", portfolioCash)
                    .Replace("{current_positions_json}", string.IsNullOrWhiteSpace(currentPositionsJson) || currentPositionsJson == "[]" ? "No current positions." : currentPositionsJson)
                    .Replace("{news_summary}", newsSummary)
                    .Replace("{relevant_symbols_list}", symbolsToAnalyzeList)
                    .Replace("{market_data_summary_json}", marketDataSummaryJson)
                    .Replace("{symbols_to_analyze_list}", symbolsToAnalyzeList);

                _logger.LogDebug("RunId: {RunId} - Constructed Claude User Prompt (to be sent via Conductor):\n{Prompt}", runId, finalUserPromptForClaude);

                // 5. Get Trading Decisions from AI (via Conductor, which then calls Athena)
                _logger.LogInformation("RunId: {RunId} - Requesting AI trading decisions via Conductor.", runId);
                // VERIFY: Ensure Temperance.Data.Models.Conductor.AiTradingDecisionPromptDto is defined
                var aiDecisionPromptDto = new AiTradingDecisionPromptDto
                {
                    UserPrompt = finalUserPromptForClaude,
                    SystemPrompt = _claudeTradingSystemPrompt
                };
                // VERIFY: Ensure Temperance.IConductorService has GetAiTradingDecisionAsync(AiTradingDecisionPromptDto)
                // VERIFY: Ensure Temperance.Data.Models.Conductor.AiTradingDecisionResponseDto and AiDecisionDto are defined
                AiTradingDecisionResponseDto? claudeOutputViaConductor = await _conductorService.GetAiTradingDecisionAsync(aiDecisionPromptDto);

                if (claudeOutputViaConductor == null || claudeOutputViaConductor.Decisions == null)
                {
                    _logger.LogWarning("RunId: {RunId} - Did not receive valid decisions from AI via Conductor. Ending cycle.", runId);
                    return;
                }
                _logger.LogInformation("RunId: {RunId} - AI Memo (via Conductor): {Memo}", runId, claudeOutputViaConductor.OverallReasoningMemo);
                // TODO: Store Memo

                // 6. Process Proposed Trades
                if (!(claudeOutputViaConductor.Decisions?.Any() ?? false))
                {
                    _logger.LogInformation("RunId: {RunId} - AI (via Conductor) proposed no trades.", runId);
                }

                foreach (var decision in claudeOutputViaConductor.Decisions ?? Enumerable.Empty<AiDecisionDto>())
                {
                    _logger.LogInformation("RunId: {RunId} - AI proposed: {Action} {Quantity} of {Symbol}. Reason: {Reasoning}",
                        runId, decision.Action, decision.Quantity, decision.Symbol, decision.Reasoning);

                    if (decision.Action == "BUY" || decision.Action == "SELL")
                    {
                        if (decision.Quantity <= 0) { /* ... skipping ... */ continue; }

                        if (decision.Action == "SELL")
                        {
                            // VERIFY: portfolio.Positions is accessible and PortfolioPositionDto has Symbol, Quantity
                            PortfolioPositionDto? positionToSell = portfolio.Positions?.FirstOrDefault(p => p.Symbol == decision.Symbol);
                            if (positionToSell == null) { /* ... skipping ... */ continue; }
                            if (decision.Quantity > positionToSell.Quantity) { decision.Quantity = positionToSell.Quantity; }
                        }
                        else if (decision.Action == "BUY")
                        {
                            _logger.LogDebug("Processing BUY decision for {Symbol}, Quantity: {Quantity}", decision.Symbol, decision.Quantity);

                            // 1. Fetch the latest quote from Conductor
                            // VERIFY: Ensure _conductorService.GetLatestQuoteAsync returns your new SimpleQuoteDto structure
                            SimpleQuoteDto? quote = await _conductorService.GetLatestQuoteAsync(decision.Symbol);

                            // 2. Validate the quote object itself
                            if (quote == null)
                            {
                                _logger.LogWarning("No quote data received from Conductor for BUY decision on {Symbol}. Skipping trade.", decision.Symbol);
                                continue;
                            }

                            // If your SimpleQuoteDto can include an error message from Conductor/Agora:
                            // if (!string.IsNullOrEmpty(quote.Error))
                            // {
                            //     _logger.LogWarning("Quote for {Symbol} contained an error: '{QuoteError}'. Skipping BUY decision.", decision.Symbol, quote.Error);
                            //     continue;
                            // }

                            // 3. Check for quote staleness
                            // You should make ACCEPTABLE_STALENESS_SECONDS configurable
                            const int ACCEPTABLE_STALENESS_SECONDS = 60;
                            TimeSpan quoteAge = DateTimeOffset.UtcNow - quote.TimestampUtc;

                            if (quoteAge.TotalSeconds > ACCEPTABLE_STALENESS_SECONDS)
                            {
                                _logger.LogWarning("Quote for {Symbol} is stale. Timestamp: {QuoteTimestamp} (Age: {QuoteAgeSeconds}s). Skipping BUY decision.",
                                    decision.Symbol, quote.TimestampUtc, quoteAge.TotalSeconds);
                                continue;
                            }
                            _logger.LogDebug("Quote for {Symbol} is fresh. AskPrice: {AskPrice}, AskSize: {AskSize}, Timestamp: {Timestamp}",
                                decision.Symbol, quote.AskPrice, quote.AskSize, quote.TimestampUtc);

                            // 4. Determine estimated price for BUY using AskPrice.
                            // Your new SimpleQuoteDto has AskPrice directly from Alpaca's quote structure.
                            decimal estimatedPrice = quote.AskPrice;

                            if (estimatedPrice <= 0)
                            {
                                _logger.LogWarning("Invalid or zero AskPrice ({AskPrice}) received for BUY decision on {Symbol}. Skipping trade.",
                                    estimatedPrice, decision.Symbol);
                                continue;
                            }

                            // 5. Basic liquidity check: Ensure shares are available at the ask and requested quantity is reasonable.
                            if (quote.AskSize <= 0)
                            {
                                _logger.LogWarning("No shares available at the AskPrice for {Symbol} (AskSize: {AskSize}). Skipping BUY decision.",
                                    decision.Symbol, quote.AskSize);
                                continue;
                            }

                            if (decision.Quantity > quote.AskSize)
                            {
                                _logger.LogWarning("Requested BUY quantity {RequestedQuantity} for {Symbol} exceeds available AskSize {AskSize}. " +
                                                   "Consider partial fill logic or adjusting quantity. For now, skipping trade for safety.",
                                                   decision.Quantity, decision.Symbol, quote.AskSize);
                                // Depending on your strategy, you might:
                                // a) Adjust the quantity: decision.Quantity = quote.AskSize; (if partial fill at best ask is acceptable)
                                // b) Skip the trade (as done here for a conservative approach)
                                // c) Attempt to get more market depth (more complex)
                                continue;
                            }

                            // 6. Calculate estimated cost
                            decimal estimatedCost = estimatedPrice * decision.Quantity;
                            _logger.LogDebug("Estimated cost for BUY {Quantity} of {Symbol} at {EstimatedPrice} is {EstimatedCost}",
                                decision.Quantity, decision.Symbol, estimatedPrice, estimatedCost);

                            // 7. Perform portfolio and risk checks
                            // VERIFY: portfolio object, portfolio.TotalValue, and portfolio.Cash are accessible and up-to-date.
                            if (portfolio == null)
                            {
                                _logger.LogError("Portfolio information is not available. Skipping BUY decision for {Symbol}.", decision.Symbol);
                                continue;
                            }

                            decimal maxTradeValue = portfolio.TotalValue * 0.01m; // Example: Max 5% of total portfolio value per trade

                            if (estimatedCost > portfolio.Cash)
                            {
                                _logger.LogWarning("Estimated cost {EstimatedCost} for BUY {Symbol} exceeds available cash {AvailableCash}. Skipping trade.",
                                    estimatedCost, decision.Symbol, portfolio.Cash);
                                continue;
                            }

                            if (estimatedCost > maxTradeValue)
                            {
                                _logger.LogWarning("Estimated cost {EstimatedCost} for BUY {Symbol} exceeds maximum allowed trade value {MaxTradeValue} (5% of portfolio). Skipping trade.",
                                    estimatedCost, decision.Symbol, maxTradeValue);
                                continue;
                            }

                            _logger.LogInformation("BUY decision for {Quantity} of {Symbol} passed pre-trade checks. Estimated cost: {EstimatedCost}",
                                decision.Quantity, decision.Symbol, estimatedCost);

                            // If all checks pass, proceed to generate the TradeRequestDto (as per your ... VERIFY ... comment)
                            // var tradeRequest = new Temperance.Data.Models.Conductor.TradeRequestDto { ... };
                            // ... rest of your logic to place the order via Agora ...
                        }

                        // VERIFY: Ensure Temperance.Data.Models.Conductor.TradeRequestDto is defined
                        var tradeRequest = new TradeRequestDto
                        {
                            Symbol = decision.Symbol,
                            Quantity = decision.Quantity,
                            Action = decision.Action,
                            OrderType = "market",
                            TimeInForce = "day"
                        };
                        // VERIFY: Ensure Temperance.IConductorService has ExecuteTradeAsync
                        // VERIFY: Ensure Temperance.Data.Models.Conductor.TradeResponseDto is defined and has Success, OrderId, Message
                        TradeResponseDto? tradeResponse = await _conductorService.ExecuteTradeAsync(tradeRequest);

                        if (tradeResponse?.Success == true)
                        {
                            _logger.LogInformation("RunId: {RunId} - Trade for {Symbol} (Order ID: {OrderId}) reported as successful by Conductor.", runId, decision.Symbol, tradeResponse.OrderId ?? "N/A");
                        }
                        else
                        {
                            _logger.LogWarning("RunId: {RunId} - Trade for {Symbol} failed or was rejected by Conductor. Message: {Message}", runId, decision.Symbol, tradeResponse?.Message ?? "No message.");
                        }
                    }
                }
                _logger.LogInformation("RunId: {RunId} - AI Trading Cycle finished at {Timestamp}", runId, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunId: {RunId} - Unhandled exception in AI Trading Cycle.", runId);
            }
        }
    }
}