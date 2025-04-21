using Dapper;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace TradingBot.Utilities.Helpers
{
	public class SqlHelper : ISqlHelper
	{
		private readonly string _connectionString;
		private readonly ConcurrentDictionary<string, SemaphoreSlim> _tableLocks = new();

        public SqlHelper(string connectionString)
		{
            _connectionString = connectionString;
		}

        public async Task<bool> TableExists(string tableName)
        {
            using var connection = new SqlConnection(_connectionString);
            var cleanTableName = tableName.Replace("[", "").Replace("]", "").Split('.').Last();
            return await connection.ExecuteScalarAsync<bool>(
                @"SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = 'Prices' 
                        AND TABLE_NAME = @TableName
                ) THEN 1 ELSE 0 END",
                new { TableName = cleanTableName });
        }

        public string SanitizeTableName(string symbol, string interval)
		{
			var cleanSymbol = Regex.Replace(symbol, @"[^a-zA-Z0-9]", "_");
			var cleanInterval = Regex.Replace(interval, @"[^a-zA-Z0-9]", "_");
			return $"[Prices].[{cleanSymbol}_{cleanInterval}]";
		}

        public async Task EnsureTableExists(string tableName)
        {
            var lockKey = tableName.ToLower();
            var tableLock = _tableLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

            await tableLock.WaitAsync();
            try
            {
                using var connection = new SqlConnection(_connectionString);

                var cleanTableName = tableName.Replace("[", "").Replace("]", "").Split('.').Last();

                var tableExists = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = 'Prices' 
                        AND TABLE_NAME = @TableName
                ) THEN 1 ELSE 0 END",
                    new { TableName = cleanTableName });

                if (!tableExists)
                {
                    await connection.ExecuteAsync($@"
                    CREATE TABLE {tableName} (
                        PriceId BIGINT IDENTITY(1,1) NOT NULL,
                        SecurityID INT NOT NULL,
                        Symbol NVARCHAR(50) NOT NULL,
                        Timestamp DATETIME2 NOT NULL,
                        OpenPrice DECIMAL(18,4) NOT NULL,
                        HighPrice DECIMAL(18,4) NOT NULL,
                        LowPrice DECIMAL(18,4) NOT NULL,
                        ClosePrice DECIMAL(18,4) NOT NULL,
                        Volume BIGINT NOT NULL,
                        TimeInterval NVARCHAR(50) NOT NULL,
                        CONSTRAINT PK_{cleanTableName} PRIMARY KEY (PriceId, SecurityID, Symbol, Timestamp)
                    )");
                }
            }
            finally
            {
                tableLock.Release();
            }
        }

    }
}