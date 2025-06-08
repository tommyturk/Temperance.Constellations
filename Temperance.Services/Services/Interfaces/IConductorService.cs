using Temperance.Data.Models.Conductor;

namespace Temperance.Services.Services.Interfaces
{
    public interface IConductorService
    {
        Task<List<string>> GetSecurities();
        Task<bool> UpdateHistoricalPrices(string symbol, string interval);

        Task<PortfolioSnapshotDto?> GetPortfolioSnapshotAsync();
        Task<List<SimpleBarDto>?> GetRecentBarsAsync(string symbol, int limit, string timeframe);
        Task<SimpleQuoteDto?> GetLatestQuoteAsync(string symbol);
        Task<TradeResponseDto?> ExecuteTradeAsync(TradeRequestDto tradeRequest);

        Task<string?> GetAiNewsSummaryAsync(AiNewsSummaryPromptDto promptDto);
        Task<AiTradingDecisionResponseDto?> GetAiTradingDecisionAsync(AiTradingDecisionPromptDto promptDto);

        Task<string> GetComplexMarketInsights(Guid runId, string symbol, string dataTypes, DateTime? startDate = null, DateTime? endDate = null,
            string? intervals = null, string? newsKeywords = null, string? sector = null);
        Task<List<string>> GetSectors();

        Task<string> GetFundamentalsFromSector(
            Guid runId,
            string? symbols = null,
            string? sectors = null
            );
        Task<string> GetGenericResponse(string symbolsPrompt);
    }
}
