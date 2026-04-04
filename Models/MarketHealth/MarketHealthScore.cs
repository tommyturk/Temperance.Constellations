namespace Temperance.Constellations.Models.MarketHealth;

public class MarketRegimeMatrix
{
    public MarketHealthScore OverallRegime { get; set; }
    public MarketMomentum ShortTermMomentum { get; set; }
    public int RawMacroScore { get; set; }
}

public enum MarketHealthScore
{
    StronglyBearish = -2,
    Bearish = -1,
    Neutral = 0,
    Bullish = 1,
    StronglyBullish = 2
}

public enum MarketMomentum { Overbought, Neutral, OversoldBounce, Crashing }
