using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Temperance.Data.Models.Backtest;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{
    public class ConductorClient : IConductorClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ConductorClient> _logger;

        public ConductorClient(HttpClient httpClient, IConfiguration configuration, ILogger<ConductorClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.BaseAddress = new Uri(configuration["ConductorSettings:BaseUrl"]
            ?? "http://conductor-api:8002/");
        }

        public async Task DispatchOptimizationBatchAsync(OptimizationBatchRequest request)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/orchestration/dispatch-batch", request);
            response.EnsureSuccessStatusCode();
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
