using Newtonsoft.Json;
using Temperance.Services.Services.Interfaces;
using Temperance.Settings.Settings;


namespace Temperance.Api.Services.Services.Implementations
{
    public class ConductorService : IConductorService
    {
        private readonly HttpClient _httpClient;
        private readonly ConductorSettings _conductorSettings;

        public ConductorService(HttpClient httpClient, ConductorSettings conductorSettings)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // Increase timeout to 5 minutes
            _conductorSettings = conductorSettings;
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
                return false; // Indicate failure
            }
        }
    }
}
