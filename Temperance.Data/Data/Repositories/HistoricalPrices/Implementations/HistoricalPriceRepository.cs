using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using Temperance.Data.Data.Repositories.Securities.Interfaces;
using Temperance.Data.Models.HistoricalPriceData;
using Temperance.Utilities.Helpers;
using TradingApp.src.Core.Models;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces;

namespace TradingApp.src.Data.Repositories.HistoricalPrices.Implementations
{
    public class HistoricalPriceRepository : IHistoricalPriceRepository
    {
        private readonly string _historicalPriceConnectionString;
        private readonly ISecuritiesOverviewRepository _securitiesOverviewRepository;
        private readonly ISqlHelper _sqlHelper;

        public HistoricalPriceRepository(string connectionString, ISecuritiesOverviewRepository securitiesOverviewRepository, ISqlHelper sqlHelper)
        {
            _historicalPriceConnectionString = connectionString;
            _securitiesOverviewRepository = securitiesOverviewRepository;
            _sqlHelper = sqlHelper;
        }

        private SqlConnection CreateConnection() => new SqlConnection(_historicalPriceConnectionString);

        public async Task<List<HistoricalPriceModel>> GetSecurityHistoricalPrices(string symbol, string interval)
        {
            await using var connection = CreateConnection();
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            await _sqlHelper.EnsureTableExists(tableName);

            string query = $"SELECT * FROM {tableName} WHERE Symbol = @Symbol AND TimeInterval = @TimeInterval ORDER BY [Timestamp]";
            var prices = await connection.QueryAsync<HistoricalPriceModel>(query, new { Symbol = symbol, TimeInterval = interval });
            return prices.ToList();
        }

        public async Task<DateTime?> GetMostRecentTimestamp(string symbol, string interval)
        {
            await using var connection = CreateConnection();
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            await _sqlHelper.EnsureTableExists(tableName);

            var query = $"SELECT TOP 1 Timestamp FROM {tableName} WHERE Symbol = @Symbol AND TimeInterval = @TimeInterval ORDER BY Timestamp DESC";
            return await connection.QueryFirstOrDefaultAsync<DateTime?>(query, new { Symbol = symbol, TimeInterval = interval });
        }

        public async Task<bool> UpdateHistoricalPrices(List<HistoricalPriceModel> prices, string symbol, string timeInterval)
        {
            if (!prices.Any()) return true;

            const int batchSize = 20000;
            var success = true;
            var securityId = await _securitiesOverviewRepository.GetSecurityId(symbol);

            for (int i = 0; i < prices.Count; i += batchSize)
            {
                var batch = prices.Skip(i).Take(batchSize).ToList();
                var batchSuccess = await InsertBatchPriceRecords(securityId, batch, timeInterval);
                success &= batchSuccess;
            }
            return success;
        }

        private async Task<bool> InsertBatchPriceRecords(int securityId, List<HistoricalPriceModel> prices, string timeInterval)
        {
            if (securityId == 0 || !prices.Any()) return false;

            var symbol = prices.First().Symbol;
            var tableName = _sqlHelper.SanitizeTableName(symbol, timeInterval);
            await _sqlHelper.EnsureTableExists(tableName);

            await using var connection = CreateConnection();
            await connection.OpenAsync();

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
                dataTable.Rows.Add(securityId, price.Symbol, price.Timestamp, price.OpenPrice, price.HighPrice, price.LowPrice, price.ClosePrice, price.Volume, timeInterval);
            }

            using var bulkCopy = new SqlBulkCopy(connection)
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
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval)
        {
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            string sql = $@"
                SELECT [OpenPrice] AS OpenPrice, [HighPrice] AS HighPrice, [LowPrice] AS LowPrice, [ClosePrice] AS ClosePrice, [Volume] AS Volume, [Timestamp] AS Timestamp
                FROM {tableName}
                ORDER BY [Timestamp] ASC;";
            await using (var connection = new SqlConnection(_historicalPriceConnectionString))
            {
                return (await connection.QueryAsync<HistoricalPriceModel>(sql)).ToList();
            }
        }


        #region Other Methods
        public Task<decimal> GetLatestPriceAsync(string symbol, DateTime backtestTimestamp)
        {
            throw new NotImplementedException();
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPricesForMonth(string symbol, string timeInterval, DateTime startDate, DateTime endDate)
        {
            await using var connection = CreateConnection();
            var parameters = new { Symbol = symbol, TimeInterval = timeInterval, StartDate = startDate, EndDate = endDate };
            var tableName = _sqlHelper.SanitizeTableName(symbol, timeInterval);
            var query = $"SELECT * FROM {tableName} WHERE Symbol = @Symbol AND TimeInterval = @TimeInterval AND Timestamp >= @StartDate AND Timestamp <= @EndDate";
            var result = await connection.QueryAsync<HistoricalPriceModel>(query, parameters);
            return result.ToList();
        }

        public async Task<List<HistoricalPriceModel>> GetHistoricalPrices(string symbol, string interval, DateTime startDate, DateTime endDate)
        {
            await using var connection = CreateConnection();
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            try
            {
                return (await connection.QueryAsync<HistoricalPriceModel>(
                    $"SELECT * FROM {tableName} WHERE Timestamp >= @StartDate AND Timestamp <= @EndDate ORDER BY Timestamp",
                    new { StartDate = startDate, EndDate = endDate })).ToList();
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return new List<HistoricalPriceModel>();
            }
        }

        public async Task<List<HistoricalPriceModel>> GetAllHistoricalPrices(List<string> symbols, List<string> intervals)
        {
            await using var connection = CreateConnection();
            var data = new List<HistoricalPriceModel>();
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
                    await _sqlHelper.EnsureTableExists(tableName);
                    var query = $"SELECT * FROM {tableName}";
                    data.AddRange(await connection.QueryAsync<HistoricalPriceModel>(query));
                }
            }
            return data.ToList();
        }

        public async Task<bool> CheckIfBackfillExists(string symbol, string interval)
        {
            await using var connection = CreateConnection();
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            await _sqlHelper.EnsureTableExists(tableName);

            var query = $"SELECT CASE WHEN (SELECT COUNT(DISTINCT YEAR([Timestamp])) FROM {tableName}) > 20 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS Result;";
            return await connection.QueryFirstOrDefaultAsync<bool>(query);
        }

        public async Task<bool> DeleteHistoricalPrices(string symbol, string interval)
        {
            await using var connection = CreateConnection();
            var tableName = _sqlHelper.SanitizeTableName(symbol, interval);
            await _sqlHelper.EnsureTableExists(tableName);
            var query = $"DROP TABLE {tableName}";
            return await connection.ExecuteAsync(query) > 0;
        }
        #endregion
    }
}