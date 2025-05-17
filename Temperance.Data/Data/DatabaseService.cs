using Microsoft.Data.SqlClient;

namespace TradingApp.src.Data
{
    public class DatabaseService
    {
        private string _connectionString = "Server=TOMMYS-WS\\SQLEXPRESS;Database=TemperanceDb;Integrated Security=True;TrustServerCertificate=True;";
        private string _historicalPriceConnectionString = "Server=TOMMYS-WS\\TRADINGBOTSERVER;Database=HistoricalPrices;Integrated Security=True;Encrypt=False";
        public async Task ConnectToDatabaseAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    Console.WriteLine("Connection to the database is successful.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("An error occurred while connecting to the database: " + ex.Message);
                }
            }
        }
        public async Task ConnectToHistoricalDatabaseAsync()
        {
            using (var connection = new SqlConnection(_historicalPriceConnectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    Console.WriteLine("Connection to the historicalPrice database is successful.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("An error occurred while connecting to the historicalPrice database: " + ex.Message);
                }
            }
        }
    }

}
