using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using Temperance.Settings;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Data.Models.Securities.BalanceSheet;
using Temperance.Data.Models.Securities.Earnings;
using Temperance.Data.Models.Securities.SecurityOverview;
using Temperance.Services.Services.Interfaces;

namespace TradingApp.src.Core.Services.Implementations
{
    public class AlphaVantageService : IAlphaVantageService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey = "Q63WJB6QXCD0USO7";

        public AlphaVantageService(HttpClient httpClient, IOptions<AlphaVantageSettings> settings)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                if (errors != System.Net.Security.SslPolicyErrors.None)
                {
                    Console.WriteLine($"SSL Certificate Error: {errors}");
                }
                return true;
            };

            _httpClient = new HttpClient(handler);
            _baseUrl = settings.Value.BaseUrl;
        }

        public async Task<SecuritySearchResponse> SearchSecurities(string query)
        {
            var url = $"{_baseUrl}?function=SYMBOL_SEARCH&keywords={query}&apikey={_apiKey}";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonConvert.DeserializeObject<SecuritySearchResponse>(response);
            return data;
        }

        public async Task<List<HistoricalPriceModel>> GetIntradayDataBatch(string symbol, string interval, string month)
        {
            var url = $"{_baseUrl}?function=TIME_SERIES_INTRADAY&symbol={symbol}" +
                      $"&interval={interval}&month={month}&outputsize=full&apikey={_apiKey}";

            Console.WriteLine($"Url: {url}");
            const int maxRetries = 3;
            const int delayBetweenRetries = 2000;

            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var jsonData = JsonConvert.DeserializeObject<JObject>(response);

                    if (jsonData == null)
                    {
                        Console.WriteLine($"No data returned from the API for {symbol} ({interval}) for {month}.");
                        return new List<HistoricalPriceModel>();
                    }

                    if (jsonData["Error Message"] != null)
                    {
                        Console.WriteLine($"API Error: {jsonData["Error Message"]}");
                        return new List<HistoricalPriceModel>();
                    }

                    var timeSeriesKey = $"Time Series ({interval})";
                    var timeSeries = jsonData[timeSeriesKey] as JObject;

                    if (timeSeries == null)
                    {
                        Console.WriteLine($"No time series data found for {symbol} ({interval}) for {month}.");
                        return new List<HistoricalPriceModel>();
                    }

                    var historicalPrices = new List<HistoricalPriceModel>();

                    foreach (var item in timeSeries.Properties())
                    {
                        try
                        {
                            var historicalPrice = new HistoricalPriceModel
                            {
                                Symbol = symbol,
                                TimeInterval = interval,
                                Timestamp = DateTime.Parse(item.Name),
                                OpenPrice = double.Parse(item.Value["1. open"]?.ToString()),
                                HighPrice = double.Parse(item.Value["2. high"]?.ToString()),
                                LowPrice = double.Parse(item.Value["3. low"]?.ToString()),
                                ClosePrice = double.Parse(item.Value["4. close"]?.ToString()),
                                Volume = long.Parse(item.Value["5. volume"]?.ToString())
                            };

                            historicalPrices.Add(historicalPrice);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing data for {symbol} at timestamp {item.Name}: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"Fetched {historicalPrices.Count} records for {symbol} ({interval}) for {month}.");
                    return historicalPrices;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"HTTP Request Error (Attempt {retry + 1}/{maxRetries}): {ex.Message}");
                    if (retry == maxRetries - 1)
                    {
                        Console.WriteLine($"Max retries reached for {symbol} ({interval}) for {month}.");
                        return new List<HistoricalPriceModel>();
                    }
                    await Task.Delay(delayBetweenRetries);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"JSON Parsing Error: {ex.Message}");
                    return new List<HistoricalPriceModel>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected Error: {ex.Message}");
                    return new List<HistoricalPriceModel>();
                }
            }

            return new List<HistoricalPriceModel>();
        }

        public async Task<List<HistoricalPriceModel>> GetIntradayData(string symbol, string interval)
        {
            var url = $"{_baseUrl}?function=TIME_SERIES_INTRADAY&symbol={symbol}" +
                      $"&interval={interval}&outputsize=compact&apikey={_apiKey}";

            Console.WriteLine($"Url: {url}");
            const int maxRetries = 3;
            const int delayBetweenRetries = 2000;

            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    var response = await _httpClient.GetStringAsync(url);
                    var jsonData = JsonConvert.DeserializeObject<JObject>(response);

                    if (jsonData == null)
                    {
                        Console.WriteLine($"No data returned from the API for {symbol} ({interval}).");
                        return new List<HistoricalPriceModel>();
                    }

                    if (jsonData["Error Message"] != null)
                    {
                        Console.WriteLine($"API Error: {jsonData["Error Message"]}");
                        return new List<HistoricalPriceModel>();
                    }

                    var timeSeriesKey = $"Time Series ({interval})";
                    var timeSeries = jsonData[timeSeriesKey] as JObject;

                    if (timeSeries == null)
                    {
                        Console.WriteLine($"No time series data found for {symbol} ({interval})");
                        return new List<HistoricalPriceModel>();
                    }

                    var historicalPrices = new List<HistoricalPriceModel>();

                    foreach (var item in timeSeries.Properties())
                    {
                        try
                        {
                            var historicalPrice = new HistoricalPriceModel
                            {
                                Symbol = symbol,
                                TimeInterval = interval,
                                Timestamp = DateTime.Parse(item.Name),
                                OpenPrice = double.Parse(item.Value["1. open"]?.ToString()),
                                HighPrice = double.Parse(item.Value["2. high"]?.ToString()),
                                LowPrice = double.Parse(item.Value["3. low"]?.ToString()),
                                ClosePrice = double.Parse(item.Value["4. close"]?.ToString()),
                                Volume = long.Parse(item.Value["5. volume"]?.ToString())
                            };

                            historicalPrices.Add(historicalPrice);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing data for {symbol} at timestamp {item.Name}: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"Fetched {historicalPrices.Count} records for {symbol} ({interval}).");
                    return historicalPrices;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"HTTP Request Error (Attempt {retry + 1}/{maxRetries}): {ex.Message}");
                    if (retry == maxRetries - 1)
                    {
                        Console.WriteLine($"Max retries reached for {symbol} ({interval}).");
                        return new List<HistoricalPriceModel>();
                    }
                    await Task.Delay(delayBetweenRetries);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"JSON Parsing Error: {ex.Message}");
                    return new List<HistoricalPriceModel>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected Error: {ex.Message}");
                    return new List<HistoricalPriceModel>();
                }
            }

            return new List<HistoricalPriceModel>();
        }


        public async Task<object> GetIntradayData(string symbol, string interval, Type responseType)
        {
            var allData = new List<HistoricalPriceModel>();
            var shouldContinue = true;
            var currentTimestamp = DateTime.UtcNow;
            int maxRequestsPerMinute = 75;
            int delayBetweenRequests = (60 * 1000) / maxRequestsPerMinute;

            while (shouldContinue)
            {
                var url = $"{_baseUrl}?function=TIME_SERIES_INTRADAY&symbol={symbol}" +
                          $"&interval={interval}&apikey={_apiKey}&outputsize=full";

                var response = await _httpClient.GetStringAsync(url);
                Console.WriteLine($"Get intraday data response: {symbol}, {interval}");

                var jsonData = JsonConvert.DeserializeObject<JObject>(response);
                var timeSeriesKey = $"Time Series ({interval})";
                var timeSeries = jsonData[timeSeriesKey] as JObject;

                if (timeSeries != null)
                {
                    foreach (var item in timeSeries)
                    {
                        var historicalPrice = new HistoricalPriceModel
                        {
                            Symbol = symbol,
                            TimeInterval = interval,
                            Timestamp = DateTime.Parse(item.Key),
                            OpenPrice = double.Parse(item.Value["1. open"].ToString()),
                            HighPrice = double.Parse(item.Value["2. high"].ToString()),
                            LowPrice = double.Parse(item.Value["3. low"].ToString()),
                            ClosePrice = double.Parse(item.Value["4. close"].ToString()),
                            Volume = long.Parse(item.Value["5. volume"].ToString())
                        };

                        allData.Add(historicalPrice);
                    }

                    var lastTimestamp = allData.Last().Timestamp;
                    shouldContinue = lastTimestamp >= currentTimestamp.AddDays(-30);
                }
                else
                {
                    Console.WriteLine($"Time Series data not found in JSON for interval: {interval}");
                    break;
                }

                Console.WriteLine("Data Count: " + allData.Count);

                if (shouldContinue)
                {
                    Console.WriteLine($"Waiting {delayBetweenRequests}ms before next request...");
                    await Task.Delay(delayBetweenRequests);
                }
            }

            return allData;
        }

        public async Task<SecuritiesOverview> GetSecuritiesOverviewData(string symbol)
        {
            var url = $"{_baseUrl}?function=OVERVIEW&symbol={symbol}&apikey={_apiKey}";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonConvert.DeserializeObject<SecuritiesOverview>(response);
            return data;
        }

        public async Task<Earnings> GetSecuritiesEarningsData(string symbol)
        {
            var url = $"{_baseUrl}?function=EARNINGS&symbol={symbol}&apikey={_apiKey}";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonConvert.DeserializeObject<Earnings>(response);
            return data;
        }

        public async Task<BalanceSheetModel> GetBalanceSheetsData(string symbol)
        {
            var url = $"{_baseUrl}?function=BALANCE_SHEET&symbol={symbol}&apikey={_apiKey}";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonConvert.DeserializeObject<BalanceSheetModel>(response);
            return data;
        }

        //private async Task EnforceRateLimitAsync()
        //{
        //    lock (_lock)
        //    {
        //        _requestCount++;
        //    }

        //    if (_requestCount >= 75)
        //    {
        //        Console.WriteLine("Rate limit reached. Pausing for 60 seconds...");
        //        await Task.Delay(TimeSpan.FromSeconds(60));

        //        lock (_lock)
        //        {
        //            _requestCount = 0;
        //        }
        //    }

        //    await Task.Delay(800);
        //}

        //public async Task<List<HistoricalPriceModel>> GetIntradayDataBatch(string symbol, string interval, string month)
        //{
        //    Console.WriteLine($"Fetching intraday data for {symbol} ({interval}) in {month}...");

        //    await EnforceRateLimitAsync();

        //    var url = $"{_baseUrl}?function=TIME_SERIES_INTRADAY&symbol={symbol}" +
        //              $"&interval={interval}&month={month}&outputsize=full&apikey={_apiKey}";

        //    var response = await _httpClient.GetStringAsync(url);
        //    var jsonData = JsonConvert.DeserializeObject<JObject>(response);
        //    var timeSeriesKey = $"Time Series ({interval})";
        //    var timeSeries = jsonData?[timeSeriesKey] as JObject;

        //    if (timeSeries == null)
        //    {
        //        Console.WriteLine($"No data available for {symbol} ({interval}) in {month}.");
        //        return new List<HistoricalPriceModel>();
        //    }

        //    var data = new List<HistoricalPriceModel>();

        //    foreach (var item in timeSeries.Properties())
        //    {
        //        data.Add(new HistoricalPriceModel
        //        {
        //            Symbol = symbol,
        //            TimeInterval = interval,
        //            Timestamp = DateTime.Parse(item.Name),
        //            OpenPrice = decimal.Parse(item.Value["1. open"].ToString()),
        //            HighPrice = decimal.Parse(item.Value["2. high"].ToString()),
        //            LowPrice = decimal.Parse(item.Value["3. low"].ToString()),
        //            ClosePrice = decimal.Parse(item.Value["4. close"].ToString()),
        //            Volume = long.Parse(item.Value["5. volume"].ToString())
        //        });
        //    }

        //    Console.WriteLine($"Fetched {data.Count} entries for {symbol} ({interval}) in {month}.");
        //    return data;
        //}

    }
}
