using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent; // Still useful for missingData
using System.Threading.Channels;     // Added
using System.Threading.Tasks;       // Added
using System;                        // Added
using System.Collections.Generic;   // Added
using System.Linq;                  // Added
using System.Threading;              // Added
//using System.Threading.RateLimiting; // Keep for basic rate limiting
using TradingBot.Data.Models.Intraday; // Keep if needed
using TradingBot.Services.Services.Interfaces;
using TradingBot.Services.Services.Implementations;
namespace TradingBot.Services.Epoch.Implementations
{
    public class HistoricalDataFetchingService : BackgroundService
    {
        private readonly IHistoricalPriceService _historicalPriceService;
        private readonly IConductorService _conductorService;
        private readonly ILogger<HistoricalDataFetchingService> _logger;
        private readonly ChannelReader<bool> _channelReader;

        public HistoricalDataFetchingService(IHistoricalPriceService historicalPriceService, IConductorService conductorService, IEarningsService earningsService)
        {
            _historicalPriceService = historicalPriceService;
            _conductorService = conductorService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (true)
            {
                Console.WriteLine("Getting symbols...");
                var symbols = await _conductorService.GetSecurities();
                Console.WriteLine("Backtesting starting...");

                Dictionary<string, Type> intervals = new Dictionary<string, Type>
            {
                { "15min", typeof(Intraday15minResponse) },
                { "30min", typeof(Intraday30minResponse) },
                { "60min", typeof(Intraday60minResponse) },
            };

                Console.WriteLine("Getting existing Data...");
                var existingData = await _historicalPriceService.GetAllHistoricalPrices(symbols, intervals.Keys.ToList());
                Console.WriteLine($"Existing Record Count: {existingData.Count}");

                var existingRecords = new HashSet<string>(existingData.Select(data =>
                    $"{data.Symbol.Trim()}_{data.TimeInterval.Trim()}_{data.Timestamp:yyyy-MM}"));

                int maxRequestsPerMinute = 50;
                int delayBetweenRequests = (60 * 1000) / maxRequestsPerMinute;
                int maxRetryAttempts = 5;

                using SemaphoreSlim semaphore = new SemaphoreSlim(maxRequestsPerMinute);
                //using RateLimiter rateLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                //{
                //    PermitLimit = maxRequestsPerMinute,
                //    Window = TimeSpan.FromMinutes(1),
                //    AutoReplenishment = true
                //});

                var totalYears = 25;
                HashSet<string> missingData = new HashSet<string>();

                foreach (var symbol in symbols)
                {
                    foreach (var interval in intervals.Keys)
                    {
                        for (int year = DateTime.UtcNow.Year - totalYears; year <= DateTime.UtcNow.Year; year++)
                        {
                            for (int month = 1; month <= 12; month++)
                            {
                                string recordKey = $"{symbol}_{interval}_{year}-{month:D2}";

                                if (!existingRecords.Contains(recordKey))
                                {
                                    missingData.Add(recordKey);
                                }
                            }
                        }
                    }
                }

                // ** Track already printed rate-limit messages **
                var printedRateLimitMessages = new ConcurrentDictionary<string, bool>();

                while (missingData.Count > 0)
                {
                    Console.WriteLine($"Remaining missing records: {missingData.Count}");

                    await Parallel.ForEachAsync(missingData.ToList(), async (recordKey, cancellationToken) =>
                    {
                        var parts = recordKey.Split('_');
                        string symbol = parts[0];
                        string interval = parts[1];
                        int year = int.Parse(parts[2].Split('-')[0]);
                        int month = int.Parse(parts[2].Split('-')[1]);

                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            //using var lease = await rateLimiter.AcquireAsync(permitCount: 1, cancellationToken);
                            //if (!lease.IsAcquired)
                            //{
                            //    string rateLimitMessageKey = $"{symbol}_{interval}_{year}-{month}";

                            //    // Print rate limit message only once per symbol-interval-year-month
                            //    printedRateLimitMessages.TryAdd(rateLimitMessageKey, true);

                            //    if (printedRateLimitMessages[rateLimitMessageKey])
                            //    {
                            //        Console.WriteLine($"Rate limit exceeded for {symbol} {interval} ({year}-{month}). Retrying...");
                            //    }

                            //    await Task.Delay(delayBetweenRequests, cancellationToken);
                            //    return;
                            //}

                            bool success = false;
                            int attempts = 0;

                            while (!success && attempts < maxRetryAttempts)
                            {
                                try
                                {
                                    Console.WriteLine($"Fetching data for {symbol} with interval {interval} ({year}-{month})...");

                                    success = await _historicalPriceService.UpdateHistoricalPrices(symbol, interval, year, month);
                                    if (!success)
                                    {
                                        Console.WriteLine($"[Attempt {attempts + 1}] Failed to fetch data for {symbol} {interval} ({year}-{month})");
                                        await Task.Delay(delayBetweenRequests * (attempts + 1), cancellationToken);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error fetching {symbol} {interval} ({year}-{month}): {ex.Message}");
                                }

                                attempts++;
                            }

                            if (success)
                            {
                                missingData.Remove(recordKey);
                                Console.WriteLine($"Missing Data count: {missingData.Count}");
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    if (missingData.Count > 0)
                    {
                        Console.WriteLine($"Retrying missing records: {missingData.Count} remaining...");
                    }
                }

                Console.WriteLine("Backtesting completed. All historical data is now available.");
                break;
            }
        }
    }
}
