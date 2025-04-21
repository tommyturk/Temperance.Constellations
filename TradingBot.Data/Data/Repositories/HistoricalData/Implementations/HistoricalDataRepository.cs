using Dapper;
using Microsoft.Data.SqlClient;
using System.Text;
using TradingBot.Data.Data.Repositories.HistoricalData.Interfaces;
using TradingBot.Data.Data.Repositories.Securities.Interfaces;

namespace TradingBot.Data.Data.Repositories.HistoricalData.Implementations
{
    public class HistoricalDataRepository : IHistoricalDataRepository
    {
        private readonly string _connectionString;

        private readonly ISecuritiesOverviewRepository _securitiesOverviewRepository;

        public HistoricalDataRepository(string connectionString, ISecuritiesOverviewRepository securitiesOverviewRepository)
        {
            _connectionString = connectionString;
            _securitiesOverviewRepository = securitiesOverviewRepository;
        }

        public async Task<bool> UpdateHistoricalPrices(List<Models.HistoricalData.HistoricalData> prices, string symbol, string timeInterval)
        {
            const int batchSize = 5000;
            var success = true;
            var batch = new List<Models.HistoricalData.HistoricalData>();
            var securityId = await _securitiesOverviewRepository.GetSecurityId(symbol);

            foreach (var price in prices)
            {
                if (await DoesRecordExist(securityId, price.Date, timeInterval))
                {
                    success &= await UpdatePriceRecord(price, timeInterval);
                    Console.WriteLine($"Successfully saved item: {price.Date}, {price.Symbol}");
                }
                else
                {
                    batch.Add(price);

                    if (batch.Count >= batchSize)
                    {
                        success &= await InsertPriceBatch(securityId, batch, timeInterval);
                        batch.Clear();
                    }
                }
            }
            if (batch.Any())
                success &= await InsertPriceBatch(securityId, batch, timeInterval);

            return success;
        }

        public async Task<bool> InsertPriceBatch(int securityId, List<Models.HistoricalData.HistoricalData> batch, string timeInterval)
        {
            try
            {
                var sql = new StringBuilder();
                var parameters = new List<object>();

                sql.Append(@"INSERT INTO [TradingBotDb].[Financials].[HistoricalPrices] 
                        (SecurityId, Symbol, Date, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume, TimeInterval, AdjustedClose, Dividends, StockSplits)
                        VALUES ");

                bool isFirstRecord = true;
                foreach (var price in batch)
                {
                    if (!isFirstRecord)
                    {
                        sql.Append(", ");
                    }

                    sql.Append("(@SecurityId, @Symbol, @Date, @OpenPrice, @HighPrice, @LowPrice, @ClosePrice, @Volume, @TimeInterval, @AdjustedClose, @Dividends, @StockSplits)");

                    parameters.Add(new
                    {
                        SecurityId = securityId,
                        price.Symbol,
                        price.Date,
                        price.OpenPrice,
                        price.HighPrice,
                        price.LowPrice,
                        price.ClosePrice,
                        price.Volume,
                        TimeInterval = timeInterval,
                    });

                    isFirstRecord = false;
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var result = await connection.ExecuteAsync(sql.ToString(), parameters.ToArray());
                    Console.WriteLine($"Successfully inserted {batch.Count} records for {securityId} at {timeInterval}");
                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting batch for {securityId} at {timeInterval}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> DoesRecordExist(int securityId, DateTime timestamp, string timeInterval)
        {
            using var connection = new SqlConnection(_connectionString);
            var parameters = new { SecurityID = securityId, Date = timestamp, TimeInterval = timeInterval };
            var query = "SELECT COUNT(1) FROM [TradingBotDb].[Financials].[HistoricalPrices] WHERE SecurityID = @SecurityID AND Date = @Date AND TimeInterval = @TimeInterval";
            return await connection.ExecuteScalarAsync<int>(query, parameters) > 0;
        }

        private async Task<bool> UpdatePriceRecord(Models.HistoricalData.HistoricalData price, string timeInterval)
        {
            using var connection = new SqlConnection(_connectionString);
            var updateQuery = @"
            UPDATE [TradingBotDb].[Financials].[HistoricalPrices] 
            SET 
                OpenPrice = @OpenPrice, HighPrice = @HighPrice, LowPrice = @LowPrice, ClosePrice = @ClosePrice, [Volume] = @Volume, [TimeInterval] = @TimeInterval, 
                [AdjustedClose] = @AdjustedClose, [Dividends] = @Dividends, [StockSplits] = @StockSplits
            WHERE 
                Symbol = @Symbol AND TimeInterval = @TimeInterval";
            var result = await connection.ExecuteAsync(updateQuery, new
            {
                price.Symbol,
                price.Date,
                TimeInterval = timeInterval,
                price.OpenPrice,
                price.HighPrice,
                price.LowPrice,
                price.ClosePrice,
                price.Volume,
            });
            return result > 0;
        }

        private async Task<bool> InsertPriceRecord(int securityId, Models.HistoricalData.HistoricalData price, string timeInterval)
        {
            securityId = securityId == 0 ? await _securitiesOverviewRepository.GetSecurityId(price.Symbol) : securityId;
            using var connection = new SqlConnection(_connectionString);
            var insertQuery = @"
                INSERT INTO [TradingBotDb].[Financials].[HistoricalPrices] 
                (SecurityID, Symbol, Date, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume, TimeInterval, AdjustedClose, Dividends, StockSplits) 
                VALUES 
                (@SecurityID, @Symbol, @Date, @OpenPrice, @HighPrice, @LowPrice, @ClosePrice, @Volume, @TimeInterval, @AdjustedClose, @Dividends, @StockSplits)";
            var result = await connection.ExecuteAsync(insertQuery, new
            {
                SecurityID = securityId,
                price.Symbol,
                price.Date,
                price.OpenPrice,
                price.HighPrice,
                price.LowPrice,
                price.ClosePrice,
                price.Volume,
                TimeInterval = timeInterval,
            });
            return result > 0;
        }

    }
}
