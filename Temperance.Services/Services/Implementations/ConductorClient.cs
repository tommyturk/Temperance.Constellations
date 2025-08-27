using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Services.Services.Implementations
{
    public class ConductorClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ConductorClient> _logger;

        public ConductorClient(HttpClient httpClient, IConfiguration configuration, ILogger<ConductorClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.BaseAddress = new Uri(configuration["ConductorApi:BaseUrl"]
                ?? "http://conductor:8080/");
        }

        public async Task NotifyBacktestCompleteAsync(BacktestCompletionPayload payload)
        {
            _logger.LogInformation("Notifying Conductor of completion for backtest RunId {RunId}", payload.RunId);
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/orchestration/backtest-completed", payload);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Successfully notified Conductor of backtest completion for RunId {RunId}", payload.RunId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify Conductor of backtest completion for RunId {RunId}", payload.RunId);
                throw;
            }
        }
    }
}
