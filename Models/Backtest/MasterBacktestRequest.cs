namespace Temperance.Constellations.Models.Backtest
{
    public record MasterBacktestRequest(
        Guid SessionId,
        DateTime StartDate,
        DateTime EndDate
    );
}
