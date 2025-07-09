using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using Temperance.Data.Models.Conductor;
using Temperance.Data.Models.Tools;
using Temperance.Services.Services.Interfaces;
using Temperance.Settings.Settings;


namespace Temperance.Services.Services.Implementations
{
    public class ConductorService : IConductorService
    {
        private readonly HttpClient _httpClient;
        private readonly ConductorSettings _conductorSettings;
        private readonly ILogger<ConductorService> _logger;

        public ConductorService(HttpClient httpClient, ConductorSettings conductorSettings, ILogger<ConductorService> logger)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _conductorSettings = conductorSettings;
            _logger = logger;
        }

        public async Task<List<string>> GetSecurities()
        {
            var url = $"{_conductorSettings.BaseUrl}/api/securities";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonConvert.DeserializeObject<List<string>>(response);
            return data;
        }

        public async Task<bool> UpdateHistoricalPrices(string symbol, string interval)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/historical?symbol={symbol}&interval={interval}";
            var response = await _httpClient.PutAsync(url, null);

            if (response.IsSuccessStatusCode)
                return true;
            else
            {
                Console.WriteLine($"Error updating historical prices for {symbol} ({interval}). Status code: {response.StatusCode}");
                return false;
            }
        }

        public async Task<PortfolioSnapshotDto?> GetPortfolioSnapshotAsync()
        {
            var url = $"{_conductorSettings.BaseUrl}/api/trading/portfolio-snapshot";
            try
            {
                _logger?.LogDebug("Requesting portfolio snapshot from Conductor API: {Url}", url);
                var responseString = await _httpClient.GetStringAsync(url);
                var data = JsonConvert.DeserializeObject<PortfolioSnapshotDto>(responseString);
                _logger?.LogDebug("Successfully fetched portfolio snapshot. Cash: {Cash}", data?.Cash);
                return data;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching portfolio snapshot from Conductor API: {Url}", url);
                return null;
            }
        }

        public async Task<List<SimpleBarDto>?> GetRecentBarsAsync(string symbol, int limit, string timeframe)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/marketdata/bars/{symbol}?limit={limit}&timeframe={timeframe}";
            try
            {
                _logger?.LogDebug("Requesting recent bars for {Symbol} from Conductor API: {Url}", symbol, url);
                var responseString = await _httpClient.GetStringAsync(url);
                var data = JsonConvert.DeserializeObject<List<SimpleBarDto>>(responseString);
                _logger?.LogDebug("Successfully fetched {Count} recent bars for {Symbol}.", data?.Count ?? 0, symbol);
                return data;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching recent bars for {Symbol} from Conductor API: {Url}", symbol, url);
                return null;
            }
        }

        public async Task<SimpleQuoteDto?> GetLatestQuoteAsync(string symbol)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/marketdata/quotes/{symbol}/latest"; 
            try
            {
                _logger?.LogDebug("Requesting latest quote for {Symbol} from Conductor API: {Url}", symbol, url);
                var responseString = await _httpClient.GetStringAsync(url);
                var data = JsonConvert.DeserializeObject<SimpleQuoteDto>(responseString);
                _logger?.LogDebug("Successfully fetched latest quote for {Symbol}. Price: {Price}", symbol, data?.AskPrice);
                return data;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching latest quote for {Symbol} from Conductor API: {Url}", symbol, url);
                return null;
            }
        }

        public async Task<TradeResponseDto?> ExecuteTradeAsync(TradeRequestDto tradeRequest)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/trading/orders";
            try
            {
                _logger?.LogDebug("Executing trade for {Symbol} ({Action}) via Conductor API: {Url}", tradeRequest.Symbol, tradeRequest.Action, url);
                var jsonPayload = JsonConvert.SerializeObject(tradeRequest);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                var responseString = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    var data = JsonConvert.DeserializeObject<TradeResponseDto>(responseString);
                    _logger?.LogInformation("Trade execution for {Symbol} ({Action}) reported by Conductor. Success: {Success}, OrderId: {OrderId}",
                        tradeRequest.Symbol, tradeRequest.Action, data?.Success, data?.OrderId);
                    return data;
                }
                else
                {
                    _logger?.LogWarning("Error response from Conductor API during trade execution for {Symbol} ({Action}). Status: {StatusCode}. Response: {ResponseString}",
                        tradeRequest.Symbol, tradeRequest.Action, response.StatusCode, responseString);
                    try
                    {
                        var errorData = JsonConvert.DeserializeObject<TradeResponseDto>(responseString);
                        if (errorData != null) return errorData;
                    }
                    catch {  }
                    return new TradeResponseDto { Success = false, Message = $"Conductor API Error: {response.StatusCode}. Body: {responseString}" };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception during trade execution for {Symbol} ({Action}) via Conductor API: {Url}", tradeRequest.Symbol, tradeRequest.Action, url);
                return new TradeResponseDto { Success = false, Message = ex.Message };
            }
        }

        public async Task<string?> GetAiNewsSummaryAsync(AiNewsSummaryPromptDto promptDto)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/news/ai-news-summary";
            try
            {
                _logger.LogDebug("Requesting AI news summary via Conductor API: {Url}", url);
                var jsonPayload = JsonConvert.SerializeObject(promptDto);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully fetched AI news summary via Conductor.");
                    return responseString; 
                }
                else
                {
                    _logger.LogWarning("Error fetching AI news summary via Conductor. Status: {StatusCode}. Response: {ResponseString}", response.StatusCode, responseString);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching AI news summary via Conductor API: {Url}", url);
                return null;
            }
        }

        public async Task<AiTradingDecisionResponseDto?> GetAiTradingDecisionAsync(AiTradingDecisionPromptDto promptDto)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/trading/ai-trading-decision";
            try
            {
                _logger.LogDebug("Requesting AI trading decision via Conductor API: {Url}", url);
                var jsonPayload = JsonConvert.SerializeObject(promptDto);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonConvert.DeserializeObject<AiTradingDecisionResponseDto>(responseString);
                    _logger.LogInformation("Successfully fetched AI trading decisions via Conductor. Decisions count: {Count}", data?.Decisions?.Count ?? 0);
                    return data;
                }
                else
                {
                    _logger.LogWarning("Error fetching AI trading decisions via Conductor. Status: {StatusCode}. Response: {ResponseString}", response.StatusCode, responseString);
                    try { return JsonConvert.DeserializeObject<AiTradingDecisionResponseDto>(responseString); }
                    catch { }
                    return new AiTradingDecisionResponseDto { Error = $"Conductor API Error: {response.StatusCode}. Body: {responseString}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching AI trading decisions via Conductor API: {Url}", url);
                return new AiTradingDecisionResponseDto { Error = ex.Message };
            }
        }

        public async Task<string> GetFundamentalsFromSector(
            Guid runId, 
            string? symbols = null,
            string? sectors = null
            )
        {
            const string toolName = "GetCompanyFundamentals";
            var url = $"{_conductorSettings.BaseUrl}/api/mcpServer/invoke-{toolName}";

            var toolArguments = new Dictionary<string, object>
            {
                { "runId", runId }
            };
            if (!string.IsNullOrWhiteSpace(symbols))
                toolArguments.Add("symbols", symbols);
            if (!string.IsNullOrWhiteSpace(sectors))
                toolArguments.Add("sectors", sectors);

            var jsonRpcRequest = new JsonRpcReqest
            {
                Params = new ToolCallParams
                {
                    Name = toolName,
                    Arguments = toolArguments
                },
                Id = Guid.NewGuid().ToString()
            };
            try
            {
                _logger.LogDebug("Requesting fundamentals from Conductor API at {Url}", url);
                var jsonPayload = JsonConvert.SerializeObject(jsonRpcRequest);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                _logger.LogTrace("JSON-RPC Payload for {ToolName}: {Payload}", toolName, jsonPayload);
                var response = await _httpClient.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Successfully invoked tool '{ToolName}' for symbols: {Symbols}.", toolName, symbols);
                    return responseString;
                }
                else
                {
                    _logger.LogWarning("Error invoking tool '{ToolName}' for symbols: {Symbols}. Status: {StatusCode}. Response: {ResponseString}",
                        toolName, symbols, response.StatusCode, responseString);
                    return $"Conductor API Error: {response.StatusCode}. Body: {responseString}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception invoking tool {ToolName} for symbols: {Symbols} via Conductor API: {Url}", toolName, symbols, url);
                return $"Exception invoking tool: {ex.Message}";
            }
        }

        public async Task<string> GetComplexMarketInsights(
            Guid runId,
            string symbol,
            string dataTypes,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? intervals = null,
            string? newsKeywords = null,
            string? sector = null)
        {
            const string toolName = "GetComplexMarketInsight";

            var url = $"{_conductorSettings.BaseUrl}/api/mcpServer/invoke-{toolName}";

            var toolArguments = new Dictionary<string, object>
            {
                { "symbol", symbol },
                { "dataTypes", dataTypes }
            };
            if (startDate.HasValue) toolArguments.Add("startDate", startDate.Value.ToString("yyyy-MM-dd"));
            if (endDate.HasValue) toolArguments.Add("endDate", endDate.Value.ToString("yyyy-MM-dd"));
            if (!string.IsNullOrWhiteSpace(intervals)) toolArguments.Add("intervals", intervals);
            if (!string.IsNullOrWhiteSpace(newsKeywords)) toolArguments.Add("newsKeywords", newsKeywords);
            if (!string.IsNullOrWhiteSpace(sector)) toolArguments.Add("sector", sector);

            toolArguments.Add("runId", runId);

            var jsonRpcRequest = new JsonRpcReqest
            {
                Params = new ToolCallParams
                {
                    Name = toolName,
                    Arguments = toolArguments
                },
                Id = new Guid().ToString()
            };

            try
            {
                _logger.LogDebug("Requesting complex market insights from Conductor API at {Url}", url);

                var jsonPayload = JsonConvert.SerializeObject(jsonRpcRequest);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogTrace("JSON-RPC Payload for {ToolName}: {Payload}", toolName, jsonPayload);

                var response = await _httpClient.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Successfully invoked tool '{ToolName}' for {Symbol}.", toolName, symbol);
                    return responseString;
                }
                else
                {
                    _logger.LogWarning("Error invoking tool '{ToolName}' for {Symbol}. Status: {StatusCode}. Response: {ResponseString}",
                        toolName, symbol, response.StatusCode, responseString);
                    return $"Conductor API Error: {response.StatusCode}. Body: {responseString}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception invoking tool {ToolName} for {Symbol} via Conductor API: {Url}", toolName, symbol, url);
                return $"Exception invoking tool: {ex.Message}";
            }
        }

        public async Task<List<string>> GetSectors()
        {
            var url = $"{_conductorSettings.BaseUrl}/api/securities/sectors"; 
            try
            {
                _logger.LogDebug("Requesting sectors from Conductor API: {Url}", url);
                var responseString = await _httpClient.GetStringAsync(url);
                var result = JsonConvert.DeserializeObject<SectorResponse>(responseString);
                _logger.LogDebug("Successfully fetched {Count} sectors.", result?.Data?.Count ?? 0);
                return result?.Data ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching sectors from Conductor API: {Url}", url);
                return new List<string>();
            }
        }

        public async Task<string> GetGenericResponse(string symbolsPrompt)
        {
            var url = $"{_conductorSettings.BaseUrl}/api/ai/generic-response";

            var payload = new
            {
                Context = symbolsPrompt
            };

            try
            {
                _logger.LogDebug("Requesting generic AI response from Conductor API: {Url}", url);
                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Successfully fetched generic AI response.");
                    return responseString;
                }
                else
                {
                    _logger.LogWarning("Error fetching generic AI response. Status: {StatusCode}. Response: {ResponseString}", response.StatusCode, responseString);
                    return $"Conductor API Error: {response.StatusCode}. Body: {responseString}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching generic AI response via Conductor API: {Url}", url);
                return $"Exception fetching generic AI response: {ex.Message}";
            }
        }
    }
}