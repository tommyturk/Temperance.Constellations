using Polly;
using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Text;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces;
using Temperance.Data.Models.HistoricalData;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Services.Services.Interfaces;
using Temperance.Utilities.Common;

namespace TradingApp.src.Core.Services.Implementations
{
    public class HistoricalPriceService : IHistoricalPriceService
    {
        private readonly IHistoricalPriceRepository _historicalPricesRepository;
        private readonly IAlphaVantageService _alphaVantageService;
        private readonly ISecuritiesOverviewService _securitiesOverviewService;
        private readonly ConcurrentDictionary<string, (Task task, CancellationTokenSource cts, BackfillStatus status)> _activeBackfills = new();

        public HistoricalPriceService(
            IHistoricalPriceRepository historicalPricesRepository,
            IAlphaVantageService alphaVantageService,
            ISecuritiesOverviewService securitiesOverviewService)
        {
            _historicalPricesRepository = historicalPricesRepository;
            _alphaVantageService = alphaVantageService;
            _securitiesOverviewService = securitiesOverviewService;
        }

        public async Task<bool> RunBacktestAsync(string symbol, string interval)
        {
            bool checkIfBackfillExists = await _historicalPricesRepository.CheckIfBackfillExists(symbol, interval);
            if (checkIfBackfillExists)
                return checkIfBackfillExists;

            Console.WriteLine("Active Back Fills: ", _activeBackfills);

            var cts = new CancellationTokenSource();
            var status = new BackfillStatus
            {
                Symbol = symbol,
                Interval = interval,
                Status = BackfillState.Running,
                StartTime = DateTime.UtcNow,
                ProgressPercentage = 0
            };

            var task = Task.Run(() => RunBacktestInternalAsync(symbol, interval, cts.Token, status), cts.Token);

            return true;
        }

        public async Task RunBacktestInternalAsync(string symbol, string interval, CancellationToken ct, BackfillStatus status)
        {
            // Create a semaphore with a count of 1 (or a higher number if you want some parallelism).
            var semaphore = new SemaphoreSlim(1, 1);

            try
            {
                var securityId = await _securitiesOverviewService.GetSecurityId(symbol);
                var existingData = await _historicalPricesRepository.GetSecurityHistoricalPrices(symbol, interval);

                var totalYears = 25;
                var monthsProcessed = 0;
                var totalMonths = totalYears * 12;
                var policy = Policy.Handle<Exception>()
                                   .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

                var tasks = new List<Task>();

                for (int year = DateTime.UtcNow.Year - totalYears; year <= DateTime.UtcNow.Year; year++)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    for (int month = 1; month <= 12; month++)
                    {
                        var localYear = year;
                        var localMonth = month;

                        if (existingData.Any(d => d.Timestamp.Year == localYear && d.Timestamp.Month == localMonth))
                            continue;

                        tasks.Add(Task.Run(async () =>
                        {
                            await semaphore.WaitAsync(ct);
                            try
                            {
                                await policy.ExecuteAsync(async () =>
                                {
                                    var data = await _alphaVantageService.GetIntradayDataBatch(symbol, interval, $"{localYear}-{localMonth:D2}");
                                    if (data != null)
                                    {
                                        await _historicalPricesRepository.UpdateHistoricalPrices(data, symbol, interval);
                                    }
                                });

                                Interlocked.Increment(ref monthsProcessed);
                                status.ProgressPercentage = (double)monthsProcessed / totalMonths * 100;

                                await Task.Delay(TimeSpan.FromSeconds(12), ct);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, ct));
                    }
                }

                await Task.WhenAll(tasks);

                status.Status = BackfillState.Completed;
            }
            catch (OperationCanceledException)
            {
                status.Status = BackfillState.Cancelled;
            }
            catch (Exception ex)
            {
                status.Status = BackfillState.Failed;
                status.ErrorMessage = ex.Message;
            }
            finally
            {
                status.EndTime = DateTime.UtcNow;
            }
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval)
        {
            var data = await _historicalPricesRepository.GetSecurityHistoricalPrices(symbol, interval);
            return data;
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval, DateTime startDate, DateTime endDate)
        {
            return await _historicalPricesRepository.GetHistoricalPrices(symbol, interval);
        }

        public async Task<bool> UpdateHistoricalPrices(string symbol, string interval, int year, int month)
        {
            var securityId = await _securitiesOverviewService.GetSecurityId(symbol);
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            var existingData = await _historicalPricesRepository.GetHistoricalPricesForMonth(symbol, interval, startDate, endDate);
            if (existingData.Any(d => d.Timestamp.Year == year && d.Timestamp.Month == month))
                return false;
            var data = await _alphaVantageService.GetIntradayDataBatch(symbol, interval, $"{year}-{month:D2}");
            if (data != null)
            {
                await _historicalPricesRepository.UpdateHistoricalPrices(data, symbol, interval);
                return true;
            }
            return false;
        }

        public async Task<bool> UpdateHistoricalPrices(List<HistoricalPriceModel> latestData, string symbol, string interval)
        {
            var lastSavedTimestamp = await _historicalPricesRepository.GetMostRecentTimestamp(symbol, interval);

            if (lastSavedTimestamp == null)
            {
                Console.WriteLine($"New security with timeframe: {interval} added to database: {symbol}");
                await _historicalPricesRepository.UpdateHistoricalPrices(latestData, symbol, interval);
                return true;
            }

            var newData = latestData.Where(d => d.Timestamp > lastSavedTimestamp).ToList();

            if (!newData.Any())
                return false; 

            await _historicalPricesRepository.UpdateHistoricalPrices(newData, symbol, interval);
            return true;
        }

        public IEnumerable<BackfillStatus> GetActiveBackfills()
        {
            return _activeBackfills.Values.Select(kvp => kvp.status);
        }

        public bool CancelBackfill(string symbol, string interval)
        {
            var key = $"{symbol}-{interval}";
            if (_activeBackfills.TryGetValue(key, out var backfill))
            {
                backfill.cts.Cancel();
                backfill.status.Status = BackfillState.Cancelled;
                backfill.status.EndTime = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        public async Task<List<HistoricalPriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals)
        {
            var task = Task.Run(() => _historicalPricesRepository.GetAllHistoricalPrices(symbols, intervals));

            return task.Result;
        }

        public async Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval)
        {
            return await _historicalPricesRepository.GetMostRecentTimestamp(symbol, interval);
        }

        public async Task<List<HistoricalPriceModel>> GetIntradayData(string symbol, string interval, DateTime? lastSavedTimestamp)
        {
            var data = await _alphaVantageService.GetIntradayData(symbol, interval);

            if (lastSavedTimestamp.HasValue)
            {
                data = data.Where(d => d.Timestamp > lastSavedTimestamp.Value).ToList();
            }

            return data;
        }

        public async Task<bool> DeleteHistoricalPrices(string symbol, string interval)
        {
            return await _historicalPricesRepository.DeleteHistoricalPrices(symbol, interval);
        }
    }
}