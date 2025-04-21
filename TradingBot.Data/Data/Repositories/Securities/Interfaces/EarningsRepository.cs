using Dapper;
using Microsoft.Data.SqlClient;
using TradingBot.Data.Data.Repositories.Securities.Implementations;
using TradingBot.Data.Models.Securities.Earnings;

namespace TradingBot.Data.Data.Repositories.Securities.Interfaces
{
    public class EarningsRepository : IEarningsRepository
    {
        public readonly string _connectionString;

        public EarningsRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<Earnings> GetSecurityEarningsData(int securityId)
        {
            using var connection = new SqlConnection(_connectionString);

            var query = @"
                SELECT TOP(4) 
                    [ID]
                    ,[SecurityID]
                    ,[Symbol]
                    ,[FiscalDateEnding]
                    ,[ReportedDate]
                    ,[ReportedEPS]
                    ,[Surprise]
                    ,[SurprisePercentage]
                    ,[ReportTime]
                    ,[CreatedAt]
                FROM [TradingBotDb].[Financials].[QuarterlyEarnings]
                WHERE SecurityID = @SecurityId
                ORDER BY ReportedDate DESC";

            var parameters = new { SecurityId = securityId };

            var quarterlyData = (await connection.QueryAsync<QuarterlyEarnings>(query, parameters)).ToList();

            var annualQuery = @"
                SELECT TOP(4) 
                    [SecurityID]
                    ,[Symbol]
                    ,[FiscalDateEnding]
                    ,[ReportedEPS]
                    ,[CreatedAt]
                FROM [TradingBotDb].[Financials].[AnnualEarnings]
                WHERE SecurityID = @SecurityId
                ORDER BY FiscalDateEnding DESC";

            var annualData = (await connection.QueryAsync<AnnualEarnings>(annualQuery, parameters)).ToList();

            var earningsData = new Earnings
            {
                Annual = annualData,
                Quarterly = quarterlyData,
                Symbol = annualData.FirstOrDefault()?.Symbol ?? quarterlyData.FirstOrDefault()?.Symbol
            };

            return earningsData;
        }

        public async Task<bool> InsertSecuritiesEarningsData(int securityId, Earnings reports, string symbol)
        {
            var annualSuccess = false;
            var quarterlySuccess = false;
            if (reports.Annual != null)
                annualSuccess = await InsertAnnualEarnings(securityId, reports.Annual, symbol);
            if (reports.Quarterly != null)
                quarterlySuccess = await InsertQuarterlyEarnings(securityId, reports.Quarterly, symbol);

            Console.WriteLine($"Saving Annual Earnings Status: {annualSuccess}");
            Console.WriteLine($"Saving Quarterly Earnings Status: {quarterlySuccess}");
            return annualSuccess || quarterlySuccess;
        }

        public async Task<bool> InsertAnnualEarnings(int securityId, List<AnnualEarnings> reports, string symbol)
        {
            var existsCheck = await CheckIfEarningsExists(securityId, reports);
            if (existsCheck)
                return false;

            using var connection = new SqlConnection(_connectionString);
            var query = @"
                INSERT INTO [TradingBotDb].[Financials].[AnnualEarnings] 
                (SecurityID, Symbol, FiscalDateEnding, ReportedEPS, CreatedAt)
                VALUES 
                (@SecurityID, @Symbol, @FiscalDateEnding, @ReportedEPS, @CreatedAt)";
            var parameters = reports.Select(report => new
            {
                SecurityId = securityId,
                Symbol = symbol,
                report.FiscalDateEnding,
                report.ReportedEPS,
                CreatedAt = DateTime.UtcNow,
            });

            return await connection.ExecuteAsync(query, parameters) > 0;
        }

        public async Task<bool> CheckIfEarningsExists(int securityId, List<AnnualEarnings> reports)
        {
            using var connection = new SqlConnection(_connectionString);

            var query = @"
                SELECT COUNT(1) 
                FROM [TradingBotDb].[Financials].[AnnualEarnings]
                WHERE SecurityID = @SecurityId AND FiscalDateEnding = @FiscalDateEnding";

            foreach (var report in reports)
            {
                var exists = await connection.ExecuteScalarAsync<int>(query, new
                {
                    SecurityId = securityId,
                    report.FiscalDateEnding
                });

                if (exists > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> InsertQuarterlyEarnings(int securityId, List<QuarterlyEarnings> reports, string symbol)
        {
            var existsCheck = await CheckIfQuarterlyEarningsExists(securityId, reports);
            if (existsCheck)
                return existsCheck;

            var connection = new SqlConnection(_connectionString);
            var query = @"
                INSERT INTO [TradingBotDb].[Financials].[QuarterlyEarnings] 
                (
                    SecurityID,
                    Symbol,
                    FiscalDateEnding,
                    ReportedDate,
                    ReportedEPS,
                    Surprise,
                    SurprisePercentage,
                    ReportTime,
                    CreatedAt
                )
                VALUES 
                (
                    @SecurityID, 
                    @Symbol, 
                    @FiscalDateEnding,
                    @ReportedDate,
                    @ReportedEPS,
                    @Surprise,
                    @SurprisePercentage,
                    @ReportTime,
                    @CreatedAt
                )";


            var parameters = reports.Select(report => new
            {
                SecurityID = securityId,
                Symbol = symbol,
                report.FiscalDateEnding,
                report.ReportedDate,
                report.ReportedEPS,
                report.Surprise,
                report.SurprisePercentage,
                report.ReportTime,
                CreatedAt = DateTime.UtcNow,
            });

            return await connection.ExecuteAsync(query, parameters) > 0;
        }

        public async Task<bool> CheckIfQuarterlyEarningsExists(int securityId, List<QuarterlyEarnings> reports)
        {
            using var connection = new SqlConnection(_connectionString);

            var query = @"
                SELECT COUNT(1) 
                FROM [TradingBotDb].[Financials].[QuarterlyEarnings]
                WHERE SecurityID = @SecurityId AND FiscalDateEnding = @FiscalDateEnding";

            foreach (var report in reports)
            {
                var exists = await connection.ExecuteScalarAsync<int>(query, new
                {
                    SecurityId = securityId,
                    report.FiscalDateEnding
                });
                if (exists > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
