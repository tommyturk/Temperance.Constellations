using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using System.Globalization;
using Temperance.Data.Data.Maps;
using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Services.Services.Interfaces;

namespace Temperance.Services.Services.Implementations
{ public class MarketDataService : IMarketDataService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "http://localhost:8000";

        public MarketDataService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(360);
        }

        public async Task<List<HistoricalData>> GetMultipleSecurityMarketData(List<string> symbols, string timeInterval)
        {
            string symbolsQuery = string.Join(",", symbols);
            string url = $"{BaseUrl}/historical/?symbols={symbolsQuery}&interval={timeInterval}";

            var response = await _httpClient.GetStringAsync(url);
            if (string.IsNullOrEmpty(response))
                return new List<HistoricalData>();

            var modelResponse = JsonConvert.DeserializeObject<HistoricalDataResponse>(response);
            var csvData = modelResponse.Data;

            using var reader = new StringReader(csvData);
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            };

            using var csv = new CsvReader(reader, csvConfig);
            csv.Context.RegisterClassMap<HistoricalDataMap>();

            List<HistoricalData> records = csv.GetRecords<HistoricalData>().ToList();
            return records;
        }

        public async Task<List<HistoricalData>> GetMarketData(string symbol, string interval)
        {
            string url = $"{BaseUrl}/historical/{symbol}/{interval}";
            var response = await _httpClient.GetStringAsync(url);
            if(string.IsNullOrEmpty(response))
                return new List<HistoricalData>();
            var modelResponse = JsonConvert.DeserializeObject<HistoricalDataResponse>(response);
            var csvData = modelResponse.Data;

            using var reader = new StringReader(csvData);
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            };

            using var csv = new CsvReader(reader, csvConfig);
            csv.Context.RegisterClassMap<HistoricalDataMap>();

            List<HistoricalData> records = csv.GetRecords<HistoricalData>().ToList();
            return records;
        }
    }
}
