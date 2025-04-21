using Dapper;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Data;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces;
using TradingBot.Data.Data.Repositories.Securities.Interfaces;
using TradingBot.Data.Models.HistoricalPriceData;
using TradingBot.Utilities.Helpers;

namespace TradingApp.src.Data.Repositories.HistoricalPrices.Implementations
{
    public class HistoricalPriceRepository : IHistoricalPriceRepository
    {
        private readonly string _historicalPriceConnectionString;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _tableLocks = new();
        private readonly ISecuritiesOverviewRepository _securitiesOverviewRepository;
        private readonly ISqlHelper _sqlHelper;
        private readonly SqlConnection _connection;
        public HistoricalPriceRepository(string connectionString, ISecuritiesOverviewRepository securitiesOverviewRepository, 
            ISqlHelper sqlHelpler)
        {
            _historicalPriceConnectionString = connectionString;
            _securitiesOverviewRepository = securitiesOverviewRepository;
            _sqlHelper = sqlHelpler;
            _connection = new SqlConnection(_historicalPriceConnectionString);
            _connection.Open();
        }

        public async Task<List<HistoricalPriceModel>> GetSecurityHistoricalPrices(string symbol, string interval)
        {
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);

            _sqlHelper.EnsureTableExists(tableName);

            string query = $@"
                SELECT *
                FROM {tableName}
                WHERE Symbol = @Symbol AND TimeInterval = @TimeInterval
                ORDER BY [Timestamp] DESC";

            var prices = await _connection.QueryAsync<HistoricalPriceModel>(query, new
            {
                Symbol = symbol,
                TimeInterval = interval
            });

            return prices.ToList();
        }

        public async Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval)
        {
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);

            await _sqlHelper.EnsureTableExists(tableName);

            

            var query = $"SELECT TOP 1 Timestamp FROM {tableName} " +
                        "WHERE Symbol = @Symbol AND TimeInterval = @TimeInterval " +
                        "ORDER BY Timestamp DESC";

            var result = await _connection.QueryFirstOrDefaultAsync<DateTime?>(query, new { Symbol = symbol, TimeInterval = interval });
            await _connection.DisposeAsync();
            return result;
        }

        public async Task<decimal> GetLatestPriceAsync(string symbol, DateTime backtestTimestamp)
        {
            //using var connection = new SqlConnection(_connectionString);
            //string query = @"
            //SELECT TOP 1 ClosePrice 
            //FROM [TradingBotDb].[Financials].[HistoricalPrices]
            //WHERE Symbol = @Symbol AND Timestamp <= @BacktestTimestamp
            //ORDER BY Timestamp DESC"
            //;

            //var latestPrice = await _connection.QueryFirstOrDefaultAsync<decimal>(
            //    query,
            //    new { Symbol = symbol, BacktestTimestamp = backtestTimestamp }
            //);

            //if (latestPrice == 0)
            //    throw new Exception($"No historical data found for {symbol} before {backtestTimestamp}.");

            //return latestPrice;
            return new decimal();
        }

        public async Task<bool> UpdateHistoricalPrices(List<HistoricalPriceModel> prices, string symbol, string timeInterval)
        {
            var success = true;
            const int batchSize = 20000;

            Console.WriteLine($"Starting update process for symbol {symbol} with {prices.Count} prices at {DateTime.UtcNow}");

            var securityId = await _securitiesOverviewRepository.GetSecurityId(symbol);

            for (int i = 0; i < prices.Count; i += batchSize)
            {
                var batch = prices.Skip(i).Take(batchSize).ToList();

                Console.WriteLine($"Processing batch {i / batchSize + 1} with {batch.Count} records for symbol {symbol}.");

                var batchSuccess = await InsertBatchPriceRecords(securityId, batch, timeInterval);
                success &= batchSuccess;

                if (!batchSuccess)
                {
                    Console.WriteLine($"Batch {i / batchSize + 1} failed for symbol {symbol}.");
                }
            }

            Console.WriteLine($"Update process for symbol {symbol} completed at {DateTime.UtcNow}. Success: {success}");
            return success;
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPricesForMonth(string symbol, string timeInterval, DateTime startDate, DateTime endDate)
        {
            
            var parameters = new { Symbol = symbol, TimeInterval = timeInterval, StartDate = startDate, EndDate = endDate };
            var tableName = _sqlHelper.SanitizeTableName(symbol, timeInterval);
            var query = $"SELECT * FROM {tableName} " +
                "WHERE Symbol = @Symbol AND TimeInterval = @TimeInterval AND Timestamp >= @StartDate AND Timestamp <= @EndDate";
            var result = await _connection.QueryAsync<HistoricalPriceModel>(query, parameters);

            return result.ToList();
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval)
        {
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);

            

            try
            {
                return (await _connection.QueryAsync<HistoricalPriceModel>(
                    $@"SELECT * FROM {tableName} 
                   ORDER BY Timestamp")).ToList();
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return new List<HistoricalPriceModel>();
            }
        }

        private async Task<bool> DoesRecordExist(int securityId, DateTime timestamp, string timeInterval)
        {
            
            var parameters = new { SecurityID = securityId, Timestamp = timestamp, TimeInterval = timeInterval };
            var query = "SELECT COUNT(1) FROM [TradingBotDb].[Financials].[HistoricalPrices] WHERE SecurityID = @SecurityID AND Timestamp = @Timestamp AND TimeInterval = @TimeInterval";
            return await _connection.ExecuteScalarAsync<int>(query, parameters) > 0;
        }

        private async Task<bool> UpdatePriceRecord(HistoricalPriceModel price, string timeInterval)
        {
            
            await _connection.OpenAsync();

            // Start a transaction
            using var transaction = _connection.BeginTransaction();

            try
            {
                var updateQuery = @"
                    UPDATE [TradingBotDb].[Financials].[HistoricalPrices] 
                    SET OpenPrice = @OpenPrice, HighPrice = @HighPrice, LowPrice = @LowPrice, ClosePrice = @ClosePrice, Volume = @Volume, TimeInterval = @TimeInterval
                    WHERE Symbol = @Symbol AND Timestamp = @Timestamp";

                var result = await _connection.ExecuteAsync(updateQuery, new
                {
                    price.Symbol,
                    price.Timestamp,
                    TimeInterval = timeInterval,
                    price.OpenPrice,
                    price.HighPrice,
                    price.LowPrice,
                    price.ClosePrice,
                    price.Volume
                }, transaction: transaction);

                if (result > 0)
                {
                    transaction.Commit();
                    return true;
                }

                transaction.Rollback();
                return false;
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task<bool> InsertPriceRecord(int securityId, HistoricalPriceModel price, string timeInterval)
        {
            if (securityId == 0)
                securityId = await _securitiesOverviewRepository.GetSecurityId(price.Symbol);

            
            await _connection.OpenAsync();

            using var transaction = _connection.BeginTransaction();

            try
            {
                var insertQuery = @"
                    INSERT INTO [TradingBotDb].[Financials].[HistoricalPrices] (SecurityID, Symbol, Timestamp, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume, TimeInterval) 
                    VALUES (@SecurityID, @Symbol, @Timestamp, @OpenPrice, @HighPrice, @LowPrice, @ClosePrice, @Volume, @TimeInterval)";

                var result = await _connection.ExecuteAsync(insertQuery, new
                {
                    SecurityID = securityId,
                    TimeInterval = timeInterval,
                    price.Symbol,
                    price.Timestamp,
                    price.OpenPrice,
                    price.HighPrice,
                    price.LowPrice,
                    price.ClosePrice,
                    price.Volume
                }, transaction: transaction);

                if (result > 0)
                {
                    transaction.Commit();
                    return true;
                }

                transaction.Rollback();
                return false;
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task<bool> InsertBatchPriceRecords(int securityId, List<HistoricalPriceModel> prices, string timeInterval)
        {
            if (securityId == 0 || !prices.Any())
                return false;

            var symbol = prices.First().Symbol;
            var tableName = _sqlHelper.SanitizeTableName(symbol, timeInterval);

            await _sqlHelper.EnsureTableExists(tableName);

            
            await _connection.OpenAsync();

            var dataTable = new DataTable();
            dataTable.Columns.Add("SecurityID", typeof(int));
            dataTable.Columns.Add("Symbol", typeof(string));
            dataTable.Columns.Add("Timestamp", typeof(DateTime));
            dataTable.Columns.Add("OpenPrice", typeof(decimal));
            dataTable.Columns.Add("HighPrice", typeof(decimal));
            dataTable.Columns.Add("LowPrice", typeof(decimal));
            dataTable.Columns.Add("ClosePrice", typeof(decimal));
            dataTable.Columns.Add("Volume", typeof(long));
            dataTable.Columns.Add("TimeInterval", typeof(string));
            foreach (var price in prices)
            {
                dataTable.Rows.Add(
                    securityId,
                    price.Symbol,
                    price.Timestamp,
                    price.OpenPrice,
                    price.HighPrice,
                    price.LowPrice,
                    price.ClosePrice,
                    price.Volume,
                    timeInterval
                );
            }

            using var bulkCopy = new SqlBulkCopy(_connection)
            {
                DestinationTableName = tableName,
                BatchSize = 10000,
                EnableStreaming = true
            };

            bulkCopy.ColumnMappings.Add("SecurityID", "SecurityID");
            bulkCopy.ColumnMappings.Add("Symbol", "Symbol");
            bulkCopy.ColumnMappings.Add("Timestamp", "Timestamp");
            bulkCopy.ColumnMappings.Add("OpenPrice", "OpenPrice");
            bulkCopy.ColumnMappings.Add("HighPrice", "HighPrice");
            bulkCopy.ColumnMappings.Add("LowPrice", "LowPrice");
            bulkCopy.ColumnMappings.Add("ClosePrice", "ClosePrice");
            bulkCopy.ColumnMappings.Add("Volume", "Volume");
            bulkCopy.ColumnMappings.Add("TimeInterval", "TimeInterval");

            try
            {
                await bulkCopy.WriteToServerAsync(dataTable);
                Console.WriteLine($"Saved to {tableName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting into {tableName}: {ex.Message}");
                return false;
            }
        }

        public async Task<List<HistoricalPriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals)
        {
            var data = new List<HistoricalPriceModel>();
            
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
                    await _sqlHelper.EnsureTableExists(tableName);
                    var query = $"SELECT * FROM {tableName}";
                    data.AddRange(await _connection.QueryAsync<HistoricalPriceModel>(query));
                }
            }
            return data.ToList();
        }

        public async Task<bool> CheckIfBackfillExists(string symbol, string interval)
        {
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            await _sqlHelper.EnsureTableExists(tableName);
            
            var query = $"SELECT " +
                $"CASE " +
                $"  WHEN (SELECT COUNT(DISTINCT YEAR([Timestamp]))" +
                $"          FROM {tableName}) > 20" +
                $"  THEN CAST(1 AS BIT)" +
                $"  ELSE CAST(0 AS BIT)" +
                $"END AS Result;";

            var data = await _connection.QueryFirstOrDefaultAsync<bool>(query);
            return data;
        }

        public async Task<bool> DeleteHistoricalPrices(string symbol, string interval)
        {
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);

            await _sqlHelper.EnsureTableExists(tableName);

            var query = $"DROP TABLE {tableName}";
            return await _connection.ExecuteAsync(query).ContinueWith(t => t.Result > 0);
        }
    }
}
